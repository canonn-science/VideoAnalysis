using OpenCvSharp;

namespace RotationAnalysis.Core.VideoAnalysis;

/// <summary>
/// Best-effort local OCR for the jet-cone HUD distance readout ("X.XXLs"), scoped deliberately
/// narrow: only digits and a decimal point, not the full alphanumeric body name the original
/// Python tool's Claude-vision step reads. Elite's HUD font defeats generic OCR engines
/// (confirmed by the original tool's own notes), so this uses a hole-counting/shape-heuristic
/// classifier instead of a trained model or hand-curated pixel templates - both were tried during
/// development against real sample footage; hand-curated templates turned out too fragile
/// (background starfield bleed and lens-flare artifacts routinely corrupt individual glyph
/// crops), and a from-scratch classifier is at least self-documenting about *why* a read is
/// uncertain. This is intentionally a best-effort first pass, not the final word: the caller
/// always shows the source crop and lets the user confirm or correct the value (see the Jet Cone
/// Length UI), and low-confidence reads are what prompts the optional Claude vision fallback
/// (<see cref="ClaudeVisionDistanceReader"/>).
///
/// Scoped to <see cref="JetWarningOnsetDetector.BottomLeftRegion"/> crops specifically (body name
/// line above, distance line below - the lower fraction is what gets classified). Not intended
/// for the reticle region, which has a different multi-line layout; if the bottom-left readout is
/// empty, callers fall back to showing the reticle crop for manual entry rather than running this
/// reader against it.
/// </summary>
public static class HudDistanceReader
{
    // Bright orange/amber HUD text. Distinct from the thinner divider line accents at the same
    // hue by stroke width, not color - filtered out geometrically below, not by color.
    private static readonly Scalar TextHsvLower = new(5, 80, 140);
    private static readonly Scalar TextHsvUpper = new(35, 255, 255);

    /// <summary>Upscale factor applied before segmentation - small HUD glyphs at native video
    /// resolution don't have cleanly resolvable holes/contours; this brings them to a size where
    /// they do, matching what worked during manual validation against real footage.</summary>
    private const double UpscaleFactor = 4.0;

    public sealed record GlyphReading(char Character, double Confidence);

    public sealed record DistanceReading(
        double? DistanceLs,
        string RawText,
        double Confidence,
        IReadOnlyList<GlyphReading> Glyphs);

    /// <summary>The bottom-left HUD region (see <see cref="JetWarningOnsetDetector.BottomLeftRegion"/>)
    /// contains two lines - the body name, then the distance below it. Only the lower fraction is
    /// the distance line; the upper fraction is skipped so its letters aren't fed to the digit
    /// classifier as spurious glyphs.</summary>
    private const double DistanceLineTopFraction = 0.55;

    public static DistanceReading Read(Mat crop)
    {
        using var distanceLine = new Mat(crop, new Rect(0, (int)(crop.Height * DistanceLineTopFraction), crop.Width, crop.Height - (int)(crop.Height * DistanceLineTopFraction)));

        using var upscaled = new Mat();
        Cv2.Resize(distanceLine, upscaled, new Size(), UpscaleFactor, UpscaleFactor, InterpolationFlags.Cubic);

        using var hsv = new Mat();
        Cv2.CvtColor(upscaled, hsv, ColorConversionCodes.BGR2HSV);
        using var mask = new Mat();
        Cv2.InRange(hsv, TextHsvLower, TextHsvUpper, mask);

        // Opening first (erode then dilate) with a kernel sized bigger than the divider line's
        // stroke width but smaller than a glyph stroke's width removes the line without eating
        // real glyphs; closing afterward reconnects any glyph strokes the erosion fragmented.
        using var openKernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(3, 3));
        using var opened = new Mat();
        Cv2.MorphologyEx(mask, opened, MorphTypes.Open, openKernel, iterations: 2);
        using var closeKernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(9, 9));
        using var closed = new Mat();
        Cv2.MorphologyEx(opened, closed, MorphTypes.Close, closeKernel);

        Cv2.FindContours(closed, out var contours, out var hierarchy, RetrievalModes.CComp, ContourApproximationModes.ApproxSimple);

        var candidates = new List<(Rect Box, int Index)>();
        for (int i = 0; i < contours.Length; i++)
        {
            if (hierarchy[i].Parent >= 0)
            {
                continue; // holes are handled via their parent, not as standalone glyphs
            }

            var box = Cv2.BoundingRect(contours[i]);
            double aspect = (double)box.Width / Math.Max(box.Height, 1);
            if (box.Height < 20 || aspect > 3.5)
            {
                continue; // divider-line fragments and stray noise
            }
            candidates.Add((box, i));
        }

        if (candidates.Count == 0)
        {
            return new DistanceReading(null, string.Empty, 0.0, Array.Empty<GlyphReading>());
        }

        candidates.Sort((a, b) => a.Box.X.CompareTo(b.Box.X));
        double medianHeight = Median(candidates.Select(c => (double)c.Box.Height).ToList());

        var glyphs = new List<GlyphReading>();
        foreach (var (box, index) in candidates)
        {
            glyphs.Add(ClassifyGlyph(closed, contours, hierarchy, index, box, medianHeight));
        }

        string rawText = new string(glyphs.Select(g => g.Character).ToArray());
        double confidence = glyphs.Count > 0 ? glyphs.Average(g => g.Confidence) : 0.0;
        double? distance = double.TryParse(rawText, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

        return new DistanceReading(distance, rawText, confidence, glyphs);
    }

    private static GlyphReading ClassifyGlyph(
        Mat mask, Point[][] contours, HierarchyIndex[] hierarchy, int index, Rect box, double medianGlyphHeight)
    {
        // A blob much shorter than its neighbors, with no holes, is the decimal point.
        if (box.Height < medianGlyphHeight * 0.55)
        {
            return new GlyphReading('.', 0.7);
        }

        double aspect = (double)box.Width / box.Height;

        var holes = new List<int>();
        for (int i = 0; i < contours.Length; i++)
        {
            if (hierarchy[i].Parent == index && Cv2.ContourArea(contours[i]) > 8)
            {
                holes.Add(i);
            }
        }

        if (holes.Count >= 2)
        {
            return new GlyphReading('8', 0.75);
        }

        if (holes.Count == 1)
        {
            // Vertical position of the hole's centroid within the glyph box distinguishes
            // 9 (hole in the upper portion), 6 (hole in the lower portion), and 0 (hole fills
            // most of the box either way).
            var moments = Cv2.Moments(contours[holes[0]]);
            double holeCenterY = moments.M00 > 0 ? moments.M01 / moments.M00 : box.Y + box.Height / 2.0;
            double relativeY = (holeCenterY - box.Y) / box.Height;

            if (relativeY < 0.42)
            {
                return new GlyphReading('9', 0.55);
            }
            if (relativeY > 0.58)
            {
                return new GlyphReading('6', 0.55);
            }
            return new GlyphReading('0', 0.65);
        }

        // No holes: 1, 2, 3, 5, or 7. A narrow box is unambiguously "1" - the rest are
        // distinguished with coarse top/bottom ink-density and centroid features, which is
        // inherently less reliable than the hole-count cases above (reflected in confidence).
        if (aspect < 0.5)
        {
            return new GlyphReading('1', 0.75);
        }

        using var roi = new Mat(mask, box);
        int thirdHeight = Math.Max(box.Height / 3, 1);
        using var topThird = new Mat(roi, new Rect(0, 0, box.Width, thirdHeight));
        using var bottomThird = new Mat(roi, new Rect(0, box.Height - thirdHeight, box.Width, thirdHeight));

        double topFill = Cv2.CountNonZero(topThird) / (double)(box.Width * thirdHeight);
        double bottomFill = Cv2.CountNonZero(bottomThird) / (double)(box.Width * thirdHeight);
        double topCenterX = CenterOfMassX(topThird);
        double bottomCenterX = CenterOfMassX(bottomThird);

        // "7": a dense, near-full-width top stroke with comparatively little ink below it.
        if (topFill > 0.5 && topFill > bottomFill * 1.6)
        {
            return new GlyphReading('7', 0.5);
        }

        // "3": both top and bottom strokes lean right (the digit is open on the left).
        if (topCenterX > 0.55 && bottomCenterX > 0.55)
        {
            return new GlyphReading('3', 0.4);
        }

        // "5": flat top stroke starts from the left; bottom curve leans right.
        if (topCenterX < 0.5 && bottomCenterX > 0.5)
        {
            return new GlyphReading('5', 0.4);
        }

        // Fallback guess - "2" is the remaining common case (top curve, diagonal, flat base).
        return new GlyphReading('2', 0.3);
    }

    private static double CenterOfMassX(Mat binaryRoi)
    {
        var moments = Cv2.Moments(binaryRoi, binaryImage: true);
        if (moments.M00 <= 0)
        {
            return 0.5;
        }
        return (moments.M10 / moments.M00) / binaryRoi.Width;
    }

    private static double Median(List<double> values)
    {
        if (values.Count == 0)
        {
            return 0.0;
        }
        var sorted = values.OrderBy(v => v).ToList();
        int mid = sorted.Count / 2;
        return sorted.Count % 2 == 0 ? (sorted[mid - 1] + sorted[mid]) / 2.0 : sorted[mid];
    }
}

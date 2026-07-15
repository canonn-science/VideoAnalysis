using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using VideoAnalysis.Core.VideoAnalysis.SlitScan;

namespace VideoAnalysis.App.Views;

/// <summary>Slit Scan's parameter controls, embedded directly in the tab rather than a modal
/// dialog - built from standard sliders/dropdowns (no custom canvas controls): Static/Sweep/
/// Rotational motion, optional width animation, sampling order, In/Out trim, and independent
/// output sizing. Freeform path motion, audio-reactivity, and a separate scrub-preview renderer
/// are out of scope for this pass.</summary>
public partial class SlitScanControlPanel : UserControl
{
    public SlitScanControlPanel()
    {
        InitializeComponent();
    }

    private void SlitAngleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SlitAngleValueText is not null)
        {
            SlitAngleValueText.Text = $"{e.NewValue:0}°";
        }
        UpdatePreviewOverlay();
    }

    private void SlitPositionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SlitPositionValueText is not null)
        {
            SlitPositionValueText.Text = $"{e.NewValue:0}%";
        }
        UpdatePreviewOverlay();
    }

    private void SlitWidthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SlitWidthValueText is not null)
        {
            SlitWidthValueText.Text = $"{e.NewValue:0} px";
        }
        UpdatePreviewOverlay();
    }

    private void AnimateWidthCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
    {
        if (WidthAnimationPanel is not null)
        {
            WidthAnimationPanel.Visibility = AnimateWidthCheckBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void SlitWidthEndSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SlitWidthEndValueText is not null)
        {
            SlitWidthEndValueText.Text = $"{e.NewValue:0} px";
        }
    }

    private void MotionModeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (SweepPanel is null || RotationalPanel is null || StaticModeHint is null)
        {
            return; // fires once during InitializeComponent, before the rest of the tree exists
        }

        bool isSweep = ReferenceEquals(sender, SweepModeRadio);
        bool isRotational = ReferenceEquals(sender, RotationalModeRadio);

        StaticModeHint.Visibility = !isSweep && !isRotational ? Visibility.Visible : Visibility.Collapsed;
        SweepPanel.Visibility = isSweep ? Visibility.Visible : Visibility.Collapsed;
        RotationalPanel.Visibility = isRotational ? Visibility.Visible : Visibility.Collapsed;

        // Position and angle don't apply to Rotational (which has its own Center/Radius/Direction controls).
        if (SlitPositionSlider is not null)
        {
            SlitPositionSlider.IsEnabled = !isRotational;
            SlitPositionDisabledHint.Visibility = isRotational ? Visibility.Visible : Visibility.Collapsed;
        }
        if (SlitAngleSlider is not null)
        {
            SlitAngleSlider.IsEnabled = !isRotational;
            SlitAngleDisabledHint.Visibility = isRotational ? Visibility.Visible : Visibility.Collapsed;
        }
        UpdatePreviewOverlay();
    }

    private void SweepEndSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SweepEndValueText is not null)
        {
            SweepEndValueText.Text = $"{e.NewValue:0}%";
        }
        UpdatePreviewOverlay();
    }

    private void RotationCenterXSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (RotationCenterXValueText is not null)
        {
            RotationCenterXValueText.Text = $"{e.NewValue:0}%";
        }
        UpdatePreviewOverlay();
    }

    private void RotationCenterYSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (RotationCenterYValueText is not null)
        {
            RotationCenterYValueText.Text = $"{e.NewValue:0}%";
        }
        UpdatePreviewOverlay();
    }

    private void RotationRadiusSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (RotationRadiusValueText is not null)
        {
            RotationRadiusValueText.Text = $"{e.NewValue:0}%";
        }
        UpdatePreviewOverlay();
    }

    private void RotationDirectionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdatePreviewOverlay();

    private void RotationRevolutionsSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (RotationRevolutionsValueText is not null)
        {
            RotationRevolutionsValueText.Text = $"{e.NewValue:0.0}";
        }
    }

    private void SamplingOrderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RandomSeedPanel is null)
        {
            return;
        }

        var tag = (SamplingOrderCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        RandomSeedPanel.Visibility = tag == "Random" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void FrameIntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (FrameIntervalValueText is not null)
        {
            FrameIntervalValueText.Text = e.NewValue <= 1 ? "every frame" : $"every {e.NewValue:0} frames";
        }
    }

    private void InPointSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (InPointValueText is null)
        {
            return;
        }
        InPointValueText.Text = $"{e.NewValue:0}%";
        if (OutPointSlider is not null && OutPointSlider.Value <= e.NewValue)
        {
            OutPointSlider.Value = Math.Min(e.NewValue + 1, OutPointSlider.Maximum);
        }
        UpdateTrimRangeBar();
    }

    private void OutPointSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OutPointValueText is null)
        {
            return;
        }
        OutPointValueText.Text = $"{e.NewValue:0}%";
        if (InPointSlider is not null && InPointSlider.Value >= e.NewValue)
        {
            InPointSlider.Value = Math.Max(e.NewValue - 1, InPointSlider.Minimum);
        }
        UpdateTrimRangeBar();
    }

    private void TrimRangeTrack_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateTrimRangeBar();

    private void UpdateTrimRangeBar()
    {
        if (TrimRangeTrack is null || TrimRangeFill is null || InPointSlider is null || OutPointSlider is null)
        {
            return; // fires before the rest of the tree exists
        }

        double trackWidth = TrimRangeTrack.ActualWidth;
        if (trackWidth <= 0)
        {
            return;
        }

        double inFraction = InPointSlider.Value / 100.0;
        double outFraction = OutPointSlider.Value / 100.0;
        TrimRangeFill.Margin = new Thickness(inFraction * trackWidth, 0, 0, 0);
        TrimRangeFill.Width = Math.Max((outFraction - inFraction) * trackWidth, 2);
    }

    private void ScanSpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ScanSpeedValueText is not null)
        {
            ScanSpeedValueText.Text = $"{e.NewValue:0} px/frame";
        }
    }

    private void CustomOutputSizeCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
    {
        if (OutputSizePanel is not null)
        {
            OutputSizePanel.Visibility = CustomOutputSizeCheckBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void MotionDirectionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SlitAngleSlider is null || SamplingOrderCombo is null || MotionDirectionHintText is null)
        {
            return; // fires once during InitializeComponent, before the rest of the tree exists
        }

        var tag = (MotionDirectionCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        string? hint = null;
        switch (tag)
        {
            case "LeftToRight":
                SlitAngleSlider.Value = 90;
                SelectComboTag(SamplingOrderCombo, "Forward");
                hint = "Sets Slit angle to 90° and Sampling order to Forward.";
                break;
            case "RightToLeft":
                SlitAngleSlider.Value = 90;
                SelectComboTag(SamplingOrderCombo, "Reverse");
                hint = "Sets Slit angle to 90° and Sampling order to Reverse.";
                break;
            case "TopToBottom":
                SlitAngleSlider.Value = 0;
                SelectComboTag(SamplingOrderCombo, "Forward");
                hint = "Sets Slit angle to 0° and Sampling order to Forward.";
                break;
            case "BottomToTop":
                SlitAngleSlider.Value = 0;
                SelectComboTag(SamplingOrderCombo, "Reverse");
                hint = "Sets Slit angle to 0° and Sampling order to Reverse.";
                break;
        }

        MotionDirectionHintText.Text = hint;
        MotionDirectionHintText.Visibility = hint is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>True while a preset is being applied, so the field resets and the combo
    /// reselection that <see cref="PresetCombo_SelectionChanged"/> triggers as side effects
    /// (via <see cref="ResetToDefaults"/> and <see cref="SelectComboTag"/>) don't reenter it.</summary>
    private bool _isApplyingPreset;

    private void PresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isApplyingPreset || PresetHintText is null)
        {
            return; // fires once during InitializeComponent, before the rest of the tree exists
        }

        var tag = (PresetCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        if (string.IsNullOrEmpty(tag) || tag == "None")
        {
            PresetHintText.Text = null;
            PresetHintText.Visibility = Visibility.Collapsed;
            return;
        }

        _isApplyingPreset = true;
        try
        {
            // Presets set a full parameter bundle, so start from a clean slate rather than
            // layering their values on top of whatever the user had previously dialed in.
            ResetToDefaults();
            SelectComboTag(PresetCombo, tag);
            PresetHintText.Text = ApplyPreset(tag);
            PresetHintText.Visibility = Visibility.Visible;
        }
        finally
        {
            _isApplyingPreset = false;
        }
    }

    /// <summary>Sets the full control bundle for a named result preset (see the "Preset" card in
    /// the XAML for the list) and returns the explanatory hint to show under the picker. Every
    /// field it touches remains individually editable afterward.</summary>
    private string ApplyPreset(string tag)
    {
        switch (tag)
        {
            case "Panorama":
                SetMotionMode(SlitScanMotionMode.Static);
                SlitAngleSlider.Value = 90;
                SlitPositionSlider.Value = 50;
                SlitWidthSlider.Value = 2;
                ScanSpeedSlider.Value = 2;
                FrameIntervalSlider.Value = 1;
                return "Static, angle 90°, slit width == scan speed (2px:2px) for a seamless pan-and-stitch. Pan the camera; the slit stays fixed on-screen.";

            case "MovieBarcode":
                SetMotionMode(SlitScanMotionMode.Static);
                SlitAngleSlider.Value = 90;
                SlitPositionSlider.Value = 50;
                SlitWidthSlider.Value = 1;
                ScanSpeedSlider.Value = 1;
                FrameIntervalSlider.Value = 1;
                SetCustomOutputSize(true, 1920, 1080);
                return "Static camera and subject - condenses the whole clip's color/brightness into one fixed 1920x1080 poster strip, regardless of source length.";

            case "PhotoFinish":
                SetMotionMode(SlitScanMotionMode.Static);
                SlitAngleSlider.Value = 90;
                SlitPositionSlider.Value = 50;
                SlitWidthSlider.Value = 2;
                ScanSpeedSlider.Value = 2;
                FrameIntervalSlider.Value = 1;
                return "Same width == speed ratio as Panorama, but for a static camera with a moving subject. Trim In/Out points (Sampling card) to the subject's crossing time.";

            case "StarTrailVortex":
                SetMotionMode(SlitScanMotionMode.Rotational);
                RotationCenterXSlider.Value = 50;
                RotationCenterYSlider.Value = 50;
                RotationRadiusSlider.Value = 90;
                RotationRevolutionsSlider.Value = 0.25;
                SelectComboTag(RotationDirectionCombo, "Clockwise");
                SlitWidthSlider.Value = 8;
                ScanSpeedSlider.Value = 2;
                SelectComboTag(BlendModeCombo, "Average");
                return "Rotational, locked center, slow revolutions, wide orbit, Average blend - for rotating night-sky footage.";

            case "FastSpiral":
                SetMotionMode(SlitScanMotionMode.Rotational);
                RotationRevolutionsSlider.Value = 6;
                SlitWidthSlider.Value = 1;
                ScanSpeedSlider.Value = 1;
                SelectComboTag(BlendModeCombo, "Normal");
                return "Rotational, many fast revolutions with a narrow slit for a tight, energetic spiral.";

            case "SlowVortex":
                SetMotionMode(SlitScanMotionMode.Rotational);
                RotationRevolutionsSlider.Value = 0.5;
                RotationRadiusSlider.Value = 70;
                SlitWidthSlider.Value = 10;
                ScanSpeedSlider.Value = 2;
                SelectComboTag(BlendModeCombo, "Average");
                return "Rotational, under one revolution with a wide, overlapping slit and Average blend, for a soft psychedelic wash.";

            case "PortraitWarp":
                SetMotionMode(SlitScanMotionMode.Sweep);
                SlitAngleSlider.Value = 90;
                SlitPositionSlider.Value = 0;
                SweepEndSlider.Value = 100;
                SlitWidthSlider.Value = 2;
                SelectComboTag(SweepEasingCombo, "EaseInOut");
                return "Sweep, narrow slit, Ease In-Out - the classic stretched, distorted-face slit-scan portrait look.";

            case "GhostTrail":
                SetMotionMode(SlitScanMotionMode.Static);
                SlitAngleSlider.Value = 90;
                SlitPositionSlider.Value = 50;
                SlitWidthSlider.Value = 12;
                ScanSpeedSlider.Value = 1;
                SelectComboTag(BlendModeCombo, "Average");
                return "Static, wide overlapping slit width, slow scan speed, Average blend - a directional, streaked cousin of Long Exposure mode.";

            case "Glitch":
                SetMotionMode(SlitScanMotionMode.Sweep);
                SlitAngleSlider.Value = 90;
                SlitPositionSlider.Value = 0;
                SweepEndSlider.Value = 100;
                SelectComboTag(SweepEasingCombo, "EaseIn");
                SetAnimateWidth(true);
                SlitWidthSlider.Value = 2;
                SlitWidthEndSlider.Value = 14;
                SelectComboTag(WidthEasingCombo, "EaseIn");
                return "Sweep with a sharp easing curve plus an animated slit width, simulating CRT/rolling-shutter tearing.";

            case "VerticalPanorama":
                SetMotionMode(SlitScanMotionMode.Static);
                SlitAngleSlider.Value = 0;
                SlitPositionSlider.Value = 50;
                SlitWidthSlider.Value = 2;
                ScanSpeedSlider.Value = 2;
                FrameIntervalSlider.Value = 1;
                return "Same push-broom stitch as Panorama, rotated 90° (angle 0°) for a vertical pan.";

            default:
                return string.Empty;
        }
    }

    /// <summary>Setting <see cref="RadioButton.IsChecked"/> is a no-op (and won't fire
    /// <see cref="MotionModeRadio_Checked"/>) if the radio is already checked, so the panel
    /// visibility it drives is applied explicitly here regardless.</summary>
    private void SetMotionMode(SlitScanMotionMode mode)
    {
        var radio = mode switch
        {
            SlitScanMotionMode.Sweep => SweepModeRadio,
            SlitScanMotionMode.Rotational => RotationalModeRadio,
            _ => StaticModeRadio,
        };
        radio.IsChecked = true;
        MotionModeRadio_Checked(radio, new RoutedEventArgs());
    }

    private void SetAnimateWidth(bool animate)
    {
        AnimateWidthCheckBox.IsChecked = animate;
        WidthAnimationPanel.Visibility = animate ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetCustomOutputSize(bool enabled, int width, int height)
    {
        CustomOutputSizeCheckBox.IsChecked = enabled;
        OutputSizePanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        OutputWidthTextBox.Text = width.ToString();
        OutputHeightTextBox.Text = height.ToString();
    }

    private static void SelectComboTag(ComboBox combo, string tag)
    {
        foreach (var obj in combo.Items)
        {
            if (obj is ComboBoxItem item && (string)item.Tag == tag)
            {
                combo.SelectedItem = item;
                return;
            }
        }
    }

    private static SlitScanEasing ParseEasing(ComboBox combo) => ((combo.SelectedItem as ComboBoxItem)?.Tag as string) switch
    {
        "EaseIn" => SlitScanEasing.EaseIn,
        "EaseOut" => SlitScanEasing.EaseOut,
        "EaseInOut" => SlitScanEasing.EaseInOut,
        _ => SlitScanEasing.Linear,
    };

    /// <summary>Set by <see cref="BuildParameters"/> when it returns null, describing what needs
    /// fixing. The offending field(s) are also given an inline red border - callers can surface
    /// this text wherever their own error UI lives (e.g. the tab's inline error message).</summary>
    public string? LastValidationError { get; private set; }

    private void ValidatedTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Clear the error highlight as soon as the user starts fixing it; full re-validation
        // happens the next time BuildParameters() runs.
        SetFieldError((TextBox)sender, false);
    }

    private void SetFieldError(TextBox box, bool hasError)
    {
        if (hasError)
        {
            box.BorderBrush = (Brush)FindResource("ValidationErrorBrush");
            box.BorderThickness = new Thickness(2);
        }
        else
        {
            box.ClearValue(BorderBrushProperty);
            box.ClearValue(BorderThicknessProperty);
        }
    }

    private void PreviewContainer_SizeChanged(object sender, SizeChangedEventArgs e) => UpdatePreviewOverlay();

    /// <summary>Loads a representative frame into the preview area (or clears it back to the
    /// placeholder if <paramref name="pngBytes"/> is null, e.g. because the frame couldn't be
    /// read) and redraws the geometry guide over it.</summary>
    public void SetPreviewFrame(byte[]? pngBytes)
    {
        if (pngBytes is null)
        {
            PreviewImage.Source = null;
            PreviewOverlayCanvas.Children.Clear();
            PreviewPlaceholderText.Text = "Preview unavailable for this video.";
            PreviewPlaceholderText.Visibility = Visibility.Visible;
            return;
        }

        var bitmap = new BitmapImage();
        using (var stream = new MemoryStream(pngBytes))
        {
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
        }
        bitmap.Freeze();

        PreviewImage.Source = bitmap;
        PreviewPlaceholderText.Visibility = Visibility.Collapsed;
        UpdatePreviewOverlay();
    }

    /// <summary>Where the (letterboxed, <c>Stretch="Uniform"</c>) preview image actually sits
    /// within its container, so overlay coordinates - computed in source-frame pixel space - can
    /// be mapped onto the right screen position regardless of the container's aspect ratio.</summary>
    private Rect GetPreviewDisplayRect()
    {
        if (PreviewImage.Source is not BitmapSource bmp || PreviewContainer.ActualWidth <= 0 || PreviewContainer.ActualHeight <= 0)
        {
            return Rect.Empty;
        }

        double scale = Math.Min(PreviewContainer.ActualWidth / bmp.PixelWidth, PreviewContainer.ActualHeight / bmp.PixelHeight);
        double displayWidth = bmp.PixelWidth * scale;
        double displayHeight = bmp.PixelHeight * scale;
        return new Rect(
            (PreviewContainer.ActualWidth - displayWidth) / 2,
            (PreviewContainer.ActualHeight - displayHeight) / 2,
            displayWidth, displayHeight);
    }

    /// <summary>Redraws the geometry guide (slit line(s) or rotation orbit) over the cached
    /// preview frame - a guide only, not a rendered preview of the actual slit-scan output.</summary>
    private void UpdatePreviewOverlay()
    {
        if (PreviewOverlayCanvas is null || PreviewImage is null || SlitAngleSlider is null || RotationalModeRadio is null)
        {
            return; // fires before the rest of the tree exists, or before a frame is loaded
        }

        PreviewOverlayCanvas.Children.Clear();

        var rect = GetPreviewDisplayRect();
        if (rect.IsEmpty)
        {
            return;
        }

        if (RotationalModeRadio.IsChecked == true)
        {
            DrawRotationalGuide(rect);
        }
        else
        {
            DrawSlitLine(rect, SlitPositionSlider.Value / 100.0, isPrimary: true);
            if (SweepModeRadio.IsChecked == true)
            {
                DrawSlitLine(rect, SweepEndSlider.Value / 100.0, isPrimary: false);
            }
        }
    }

    /// <summary>Mirrors <c>SlitScanProcessor.ExtractSlit</c>'s Static/Sweep geometry: the slit is
    /// a line through the frame at <see cref="SlitScanParameters.SlitAngleDegrees"/>, offset from
    /// center perpendicular to that angle by <paramref name="positionFraction"/> of the frame
    /// width (the processor always measures the offset against width, even at non-vertical
    /// angles, since it's computed before the frame is rotated into the slit's own axis).</summary>
    private void DrawSlitLine(Rect displayRect, double positionFraction, bool isPrimary)
    {
        if (PreviewImage.Source is not BitmapSource bmp)
        {
            return;
        }

        double frameWidth = bmp.PixelWidth;
        double frameHeight = bmp.PixelHeight;
        double scale = displayRect.Width / frameWidth;

        double angleRad = SlitAngleSlider.Value * Math.PI / 180.0;
        var direction = new Vector(Math.Cos(angleRad), Math.Sin(angleRad));
        var perpendicular = new Vector(Math.Sin(angleRad), -Math.Cos(angleRad));

        double offset = positionFraction * frameWidth - frameWidth / 2.0;
        var center = new Point(frameWidth / 2.0, frameHeight / 2.0);
        var pointOnLine = center + perpendicular * offset;

        double halfLength = Math.Max(frameWidth, frameHeight);
        var p1 = pointOnLine - direction * halfLength;
        var p2 = pointOnLine + direction * halfLength;

        var line = new Line
        {
            X1 = displayRect.X + p1.X * scale,
            Y1 = displayRect.Y + p1.Y * scale,
            X2 = displayRect.X + p2.X * scale,
            Y2 = displayRect.Y + p2.Y * scale,
            Stroke = isPrimary ? (Brush)FindResource("CanonnAccentBrush") : Brushes.White,
            StrokeThickness = isPrimary ? Math.Max(SlitWidthSlider.Value * scale, 1.5) : 1.5,
            Opacity = isPrimary ? 0.85 : 0.55,
        };
        if (!isPrimary)
        {
            line.StrokeDashArray = new DoubleCollection { 4, 3 };
        }
        PreviewOverlayCanvas.Children.Add(line);
    }

    /// <summary>Mirrors <c>SlitScanProcessor.ExtractSlit</c>'s Rotational geometry: the crop
    /// offset orbits a fixed center at a fixed radius, so the guide draws that orbit as a circle
    /// rather than trying to animate the per-frame angle.</summary>
    private void DrawRotationalGuide(Rect displayRect)
    {
        if (PreviewImage.Source is not BitmapSource bmp)
        {
            return;
        }

        double frameWidth = bmp.PixelWidth;
        double frameHeight = bmp.PixelHeight;
        double scale = displayRect.Width / frameWidth;

        double centerX = displayRect.X + RotationCenterXSlider.Value / 100.0 * frameWidth * scale;
        double centerY = displayRect.Y + RotationCenterYSlider.Value / 100.0 * frameHeight * scale;
        double radius = RotationRadiusSlider.Value / 100.0 * Math.Min(frameWidth, frameHeight) / 2.0 * scale;

        var accentBrush = (Brush)FindResource("CanonnAccentBrush");

        var orbit = new Ellipse
        {
            Width = radius * 2,
            Height = radius * 2,
            Stroke = accentBrush,
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 5, 3 },
        };
        Canvas.SetLeft(orbit, centerX - radius);
        Canvas.SetTop(orbit, centerY - radius);
        PreviewOverlayCanvas.Children.Add(orbit);

        var centerDot = new Ellipse { Width = 6, Height = 6, Fill = accentBrush };
        Canvas.SetLeft(centerDot, centerX - 3);
        Canvas.SetTop(centerDot, centerY - 3);
        PreviewOverlayCanvas.Children.Add(centerDot);

        bool clockwise = ((RotationDirectionCombo.SelectedItem as ComboBoxItem)?.Tag as string) != "CounterClockwise";
        const double startAngleRad = -Math.PI / 2; // marker starts at the top of the orbit
        var startPoint = new Point(centerX + radius * Math.Cos(startAngleRad), centerY + radius * Math.Sin(startAngleRad));
        var startDot = new Ellipse { Width = 8, Height = 8, Fill = Brushes.White };
        Canvas.SetLeft(startDot, startPoint.X - 4);
        Canvas.SetTop(startDot, startPoint.Y - 4);
        PreviewOverlayCanvas.Children.Add(startDot);

        double tangentAngleRad = startAngleRad + (clockwise ? 1 : -1) * 0.35;
        var arrowTip = new Point(centerX + radius * Math.Cos(tangentAngleRad), centerY + radius * Math.Sin(tangentAngleRad));
        PreviewOverlayCanvas.Children.Add(new Line
        {
            X1 = startPoint.X,
            Y1 = startPoint.Y,
            X2 = arrowTip.X,
            Y2 = arrowTip.Y,
            Stroke = Brushes.White,
            StrokeThickness = 2,
        });
    }

    /// <summary>Reads the current control values into a <see cref="SlitScanParameters"/>, or
    /// returns null if a text field holds an invalid value - the offending field is highlighted
    /// inline and <see cref="LastValidationError"/> is set to a message describing the problem.</summary>
    public SlitScanParameters? BuildParameters()
    {
        LastValidationError = null;
        SetFieldError(OutputWidthTextBox, false);
        SetFieldError(OutputHeightTextBox, false);
        SetFieldError(RandomSeedTextBox, false);

        int? outputWidth = null;
        int? outputHeight = null;
        if (CustomOutputSizeCheckBox.IsChecked == true)
        {
            bool widthOk = int.TryParse(OutputWidthTextBox.Text.Trim(), out var width) && width >= 10;
            bool heightOk = int.TryParse(OutputHeightTextBox.Text.Trim(), out var height) && height >= 10;
            if (!widthOk || !heightOk)
            {
                SetFieldError(OutputWidthTextBox, !widthOk);
                SetFieldError(OutputHeightTextBox, !heightOk);
                LastValidationError = "Enter valid output dimensions (at least 10 pixels each).";
                return null;
            }
            outputWidth = width;
            outputHeight = height;
        }

        int randomSeed = 0;
        var samplingOrderTag = (SamplingOrderCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        if (samplingOrderTag == "Random" && !int.TryParse(RandomSeedTextBox.Text.Trim(), out randomSeed))
        {
            SetFieldError(RandomSeedTextBox, true);
            LastValidationError = "Enter a valid integer random seed.";
            return null;
        }

        return new SlitScanParameters
        {
            SlitAngleDegrees = SlitAngleSlider.Value,
            SlitPositionFraction = SlitPositionSlider.Value / 100.0,
            SlitWidthPixels = (int)SlitWidthSlider.Value,
            WidthIsAnimated = AnimateWidthCheckBox.IsChecked == true,
            SlitWidthEndPixels = (int)SlitWidthEndSlider.Value,
            WidthEasing = ParseEasing(WidthEasingCombo),

            MotionMode = SweepModeRadio.IsChecked == true
                ? SlitScanMotionMode.Sweep
                : RotationalModeRadio.IsChecked == true
                    ? SlitScanMotionMode.Rotational
                    : SlitScanMotionMode.Static,
            SweepEndPositionFraction = SweepEndSlider.Value / 100.0,
            SweepEasing = ParseEasing(SweepEasingCombo),
            RotationCenterXFraction = RotationCenterXSlider.Value / 100.0,
            RotationCenterYFraction = RotationCenterYSlider.Value / 100.0,
            RotationRadiusFraction = RotationRadiusSlider.Value / 100.0,
            RotationRevolutions = RotationRevolutionsSlider.Value,
            RotationDirection = ((RotationDirectionCombo.SelectedItem as ComboBoxItem)?.Tag as string) == "CounterClockwise"
                ? SlitScanRotationDirection.CounterClockwise
                : SlitScanRotationDirection.Clockwise,

            SamplingOrder = samplingOrderTag switch
            {
                "Reverse" => SlitScanSamplingOrder.Reverse,
                "PingPong" => SlitScanSamplingOrder.PingPong,
                "Random" => SlitScanSamplingOrder.Random,
                _ => SlitScanSamplingOrder.Forward,
            },
            RandomSeed = randomSeed,
            FrameSamplingInterval = (int)FrameIntervalSlider.Value,
            InPointFraction = InPointSlider.Value / 100.0,
            OutPointFraction = OutPointSlider.Value / 100.0,

            ScanSpeedPixelsPerFrame = (int)ScanSpeedSlider.Value,
            BlendMode = ((BlendModeCombo.SelectedItem as ComboBoxItem)?.Tag as string) switch
            {
                "Lighten" => SlitScanBlendMode.Lighten,
                "Average" => SlitScanBlendMode.Average,
                _ => SlitScanBlendMode.Normal,
            },

            CustomOutputSize = outputWidth is not null,
            OutputWidth = outputWidth ?? 1920,
            OutputHeight = outputHeight ?? 1080,
            Interpolation = ((InterpolationCombo.SelectedItem as ComboBoxItem)?.Tag as string) switch
            {
                "Nearest" => SlitScanInterpolation.Nearest,
                "Linear" => SlitScanInterpolation.Linear,
                _ => SlitScanInterpolation.Cubic,
            },
        };
    }

    /// <summary>Restores every control to its default value, so the user can try again from a
    /// clean slate without re-uploading the video.</summary>
    public void ResetToDefaults()
    {
        // Same no-op-if-already-selected caveat as the mode radios below: clear explicitly.
        SelectComboTag(MotionDirectionCombo, "None");
        MotionDirectionHintText.Text = null;
        MotionDirectionHintText.Visibility = Visibility.Collapsed;
        SelectComboTag(PresetCombo, "None");
        PresetHintText.Text = null;
        PresetHintText.Visibility = Visibility.Collapsed;
        SlitAngleSlider.Value = 90;
        SlitPositionSlider.Value = 50;
        SlitWidthSlider.Value = 2;
        // Same no-op-if-already-false caveat as the radio buttons below - set explicitly.
        AnimateWidthCheckBox.IsChecked = false;
        WidthAnimationPanel.Visibility = Visibility.Collapsed;
        SlitWidthEndSlider.Value = 2;
        SelectComboTag(WidthEasingCombo, "Linear");

        // Setting IsChecked=true is a no-op (and won't fire the Checked handler) if it's already
        // checked, so the panel visibility is reset explicitly rather than relying on the event.
        StaticModeRadio.IsChecked = true;
        StaticModeHint.Visibility = Visibility.Visible;
        SweepPanel.Visibility = Visibility.Collapsed;
        RotationalPanel.Visibility = Visibility.Collapsed;
        SlitPositionSlider.IsEnabled = true;
        SlitPositionDisabledHint.Visibility = Visibility.Collapsed;
        SlitAngleSlider.IsEnabled = true;
        SlitAngleDisabledHint.Visibility = Visibility.Collapsed;
        SweepEndSlider.Value = 50;
        SelectComboTag(SweepEasingCombo, "Linear");
        RotationCenterXSlider.Value = 50;
        RotationCenterYSlider.Value = 50;
        RotationRadiusSlider.Value = 50;
        RotationRevolutionsSlider.Value = 1.0;
        SelectComboTag(RotationDirectionCombo, "Clockwise");

        SelectComboTag(SamplingOrderCombo, "Forward");
        RandomSeedPanel.Visibility = Visibility.Collapsed;
        RandomSeedTextBox.Text = "0";
        FrameIntervalSlider.Value = 1;
        InPointSlider.Value = 0;
        OutPointSlider.Value = 100;

        ScanSpeedSlider.Value = 2;
        SelectComboTag(BlendModeCombo, "Normal");

        CustomOutputSizeCheckBox.IsChecked = false;
        OutputSizePanel.Visibility = Visibility.Collapsed;
        OutputWidthTextBox.Text = "1920";
        OutputHeightTextBox.Text = "1080";
        SelectComboTag(InterpolationCombo, "Cubic");

        LastValidationError = null;
        SetFieldError(OutputWidthTextBox, false);
        SetFieldError(OutputHeightTextBox, false);
        SetFieldError(RandomSeedTextBox, false);

        UpdateTrimRangeBar();
        UpdatePreviewOverlay();
    }
}

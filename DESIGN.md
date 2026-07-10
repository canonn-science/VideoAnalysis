# Rotation Analysis Lab

An application used to calculate the rotation speed of planetary ring systems in Elite Dangerous, for Canonn's citizen-science ring-rotation project.

## System search

The user is presented with a screen that prompts them to select a system. It's a text box with a typeahead function: after the first three characters, a dropdown of matching systems is presented for the user to pick from.

The typeahead function calls the Spansh API to get matches:
`https://spansh.co.uk/api/systems/field_values/system_names?q={systemName}`

The data returned looks like this:
```json
{"min_max":[{"id64":20050390,"name":"Graea Hypue AA-A g0","x":-1204.53125,"y":-994.875,"z":13328.1875}, ...],"values":["Graea Hypue AA-A g0", ...]}
```

If the user picks a suggestion (or submits and the typeahead API resolved a match), the app fetches that system's full body dump from Spansh:
`https://spansh.co.uk/api/dump/{id64}`

If the system can't be resolved, an error is shown instead.

## Ring table

The dump response is parsed for every body in the system, looking for rings and belts. They're displayed in a table with, per ring/belt: body name, ring name, kind (ring/belt), inner/outer radius, width, estimated rotation period, suggested video duration, and a "Select Video…" button.

### Estimated rotation period

The estimate is Kepler's third law solved against a single representative ("nominal") radius rather than the full ring geometry:

- Nominal radius = `innerRadius + (outerRadius - innerRadius) * 3/8`
- Period = `2*pi*sqrt(nominalRadius^3 / (G * parentMassKg))`

(`RingMath.NominalRadiusMeters` / `RingMath.KeplerPeriodSeconds`.)

### Suggested video duration

`ceil(estimatedPeriodSeconds / 36 / 60)` minutes — e.g. a raw result of 13.1 minutes is displayed as 14 minutes. (`RingMath.SuggestedVideoDurationMinutes`.)

## Recording the video

Unlike the original concept for this tool — filming straight up at the rotation center from a disk orbiting a neutron star jet — the shipped app has the user film the **horizon** instead, because that framing tracks more reliably:

- Park on the ring surface in free camera, facing directly away from the central body, toward the horizon (not up at the rotation center).
- Keep the horizon roughly centered vertically in frame — asteroids/ring below, open starfield above.
- Don't zoom; use the default field of view.
- Record for at least the suggested duration (longer recordings raise confidence).

The upload prompt shows an example reference shot and these instructions, and accepts a file picker or drag-and-drop (`.mp4`, `.mkv`, `.avi`, `.mov`, `.wmv`).

## Video analysis

`HorizontalVideoAnalyzer` runs the pipeline off the UI thread:

0. **Open.** `HorizontalStarTracker` opens the video and reports its resolution, frame rate, and duration back to the UI immediately (shown in the processing window and kept visible for the rest of the run).
1. **Track.** The video is split into chunks and stars are tracked frame-to-frame within each chunk. For long estimated periods, per-frame motion is tiny, so the tracker skips real frames between optical-flow samples (up to an 8x stride) rather than processing every one — the stride is sized from the ring's Kepler estimate so the pixel shift between sampled frames stays roughly constant (targeting ~2px/step) regardless of period length. Rings under ~20 minutes stay well below the cap and sample fairly densely; only genuinely slow rotations hit the 8x ceiling.
2. **Solve per chunk.** Each chunk is fit independently by `HorizontalRotationSolver` against a fixed vertical roll axis (not a fitted in-frame center, since the horizon framing keeps the axis effectively fixed). Each chunk's solved period seeds the next chunk's solve. Chunks with fewer than 20 usable tracks, or a non-finite period, are discarded.
3. **Combine.** The raw fit is the median across all chunks that produced a usable fit. The reported observed period is that median times `HorizontalVideoAnalyzer.RateBasedBiasCorrectionFactor` - a one-sample calibration correction (see below), applied to every rate-based result, not just eligible videos.
4. **Confidence.** A heuristic, not a statistical guarantee — there's no ground truth at runtime. With 2+ chunks, it's based on the coefficient of variation across chunks' periods (they're independent measurements of one true rate, since Elite's rings rotate as a rigid disk). With only one usable chunk, it's based on agreement among that chunk's own tracked stars. Either way, a 5% relative spread maps to 0% confidence, scaling up to 100% at zero spread.

If no stars can be tracked at all, or no chunk produces a reliable fit, the user gets an explanatory error asking for a longer or clearer recording instead of a result.

### Full-rotation alignment measurement

When a recording is long enough to contain a full rotation (duration ≥ 1.2× the estimated period -
preferring the rate-based observed period above, falling back to the Kepler estimate if the rate
measurement has low confidence), `HorizontalVideoAnalyzer` also measures the period directly as a
*timing* event: the instant the star field re-aligns with reference frames from the start of the
video. This is independent of the rate method's FOV/focal-length calibration, so it's both a
better answer for eligible videos and calibration data for the rate-based method.

`FullRotationAligner` captures 5 reference frames (configurable) spread across the first ~10% of
the estimated period, then for each one searches a window around one estimated-period later for
where a masked-star-field phase correlation (`Cv2.PhaseCorrelate`, letterbox/below-horizon rows
excluded) against the reference crosses zero offset. `FullRotationMath.FitZeroCrossing` interpolates
that crossing to sub-frame precision from a local linear fit rather than just picking the closest
frame, gated on correlation strength, fit R², and not landing at the search window's edge (which
retries once with a doubled window, then gives up with a "video appears shorter than one rotation"
style reason). All timing comes from the container's actual frame timestamps
(`VideoCaptureProperties.PosMsec`), not frame index/nominal fps. With ≥3 accepted samples, the
median is the measured period and the MAD-based spread is its uncertainty; otherwise the alignment
measurement is reported as failed and the rate-based result is used alone, exactly as before.

Rather than swap the rate-based result out for the measurement on the videos where both happen to
run, the measurement is used once to correct the rate-based method itself:
`RateBasedBiasCorrectionFactor` is a flat multiplier derived from the one full-rotation ground-truth
sample collected so far (rate-based fit 530.9561807430355s vs. measured 505.89471843069344s for
Eorl Scrua AA-A h670 2 A Ring - the rate method overestimated by ~4.95%), applied to *every*
rate-based result, including videos with no full-rotation measurement at all. This is what
"observed rotation" reports everywhere (results dialog, CSV, Canonn submission) - always the
rate-based method's own output, just bias-corrected, so storage stays consistent regardless of
whether alignment ran on that particular video. The raw, uncorrected median is what's logged to the
calibration file and what "rate vs. measured" and the consistency warning compare against the
measurement - future eligible videos on different rings show whether the correction still holds or
the two drift apart again, which is the signal for refining it (or replacing a single flat factor
with something that depends on angular sweep/star count/etc.) as more calibration data accumulates.

Every eligible video (success or failure) gets a `<video filename>.calibration.json` file under
`%LocalAppData%\RotationAnalysisLab\calibration\` (`RotationCalibrationLog`/`RotationCalibrationLogWriter`)
recording video/timing metadata, the rate-based fit internals, every reference sample's accept/reject
detail, and the aggregate - a calibration dataset for later fitting the rate-based method's error
model against this ground truth. It's keyed by filename rather than kept next to the video itself,
since the video can live anywhere (a large removable drive, a temp folder that gets cleaned up),
but the calibration dataset should stay put alongside the app's other local data (`measurements.csv`,
`settings.json`). Writing it is best-effort and never fails the actual measurement. If the video is
renamed (see below), the corresponding file in the calibration folder is renamed too.

### Renaming to match the ring

If the selected video's filename doesn't already match its ring (`{ringName}{ext}`, case-insensitive,
original extension preserved), a modal dialog offers to rename it as soon as analysis starts - it
doesn't wait for analysis to finish, and analysis keeps running in the background while the dialog
is up. If `{ringName}{ext}` is already taken by another file, the suggestion bumps to
`{ringName}_v2{ext}`, `{ringName}_v3{ext}`, etc. (`VideoFileNamer`).

Dismissing the dialog leaves the file where it is. Accepting it defers the actual rename until
analysis completes and the file is no longer open for reading, then every downstream reference
(results dialog, saved CSV row) uses the new path.

## Results dialog

When processing finishes, a dialog shows: system/body/ring name, estimated vs. observed period (the bias-corrected rate-based result - see above), percent difference between them, confidence percentage, median roll angle (how far off level the horizon tracked), and how many of the available recording segments were usable. When the full-rotation alignment measurement above also succeeds, the dialog additionally shows the measured period with its uncertainty, the rate-vs-measured percent difference (corrected rate-based fit vs. the independent measurement, so you can see whether they still agree or have drifted apart for this ring), and (if they disagree by more than ~3× their combined uncertainty) a warning that the pipeline/capture settings may need checking rather than reflecting real physics. If the alignment measurement didn't run (video too short) or ran but didn't converge, a small note explains which case it was and, for the latter, why. Nothing is written to disk until the user clicks **Save to History** — cancelling discards the result.

## Measurement log

Saved measurements are appended as a row to a local CSV file with these columns (`MeasurementRecord` keeps them in this order); reading tolerates older files missing columns:

```
Timestamp
System Name
id64
x
y
z
Body Name
Body Type
Body Mass
Ring Name
Ring Type
Ring Mass
innerRadius
outerRadius
Width
estimated rotation
observed rotation
video filename
submitted
measured_period_s
measured_period_err_s
n_reference_samples
rate_vs_measured_pct_diff
```

The last four columns come from the full-rotation alignment measurement above and are blank
(null) for videos that weren't eligible for it, or where the measurement didn't converge.

Body Type/Ring Type are the subType/type strings from Spansh; Body Mass is in Earth masses; Ring
Mass is passed through as reported by Spansh. All four are blank for rows written before these
columns existed.

A "Measurement History" tab lists the logged rows and has a button to reveal the CSV file on disk.

### Migrating older CSV files

`MeasurementCsvStore` checks the file's header on construction. If it predates the "Body Type"
column, every row is read (missing columns default to blank/null, same as normal tolerant
reading) and the whole file is rewritten with the current header - so the upgrade happens once,
transparently, the first time the app runs against an old CSV, rather than a version at a time.

## Commander name

The title bar shows the commander name ("CMDR Your Name Here" until set) beside the Canonn logo.
Clicking it opens a small dialog to edit it. It's persisted as JSON at
`%LocalAppData%\RotationAnalysisLab\settings.json` (`AppSettingsStore`) and restored on launch.

## Submitting to Canonn

A measurement can be sent to Canonn from two places:

- The results dialog has a **Send to Canonn** button, independent of **Save to History** — a
  measurement can be sent, saved, both, or neither. The button disables after a successful send
  for the lifetime of that dialog.
- Each row in Measurement History has a send icon; a successful send flips it to a green check
  and persists `submitted = true` for that row.

Submission is an HTTP GET to Canonn's Google Form (`CanonnClient.SubmitAsync`), with fields mapped
to entry ids:

| Field                       | URL Parameter          |
| --------------------------- | ----------------------- |
| Commander Name              | `entry.600905391`       |
| System Name                 | `entry.1130968439`      |
| id64                        | `entry.472013560`       |
| x                           | `entry.1151578825`      |
| y                           | `entry.525275561`       |
| z                           | `entry.459250128`       |
| Body Name                   | `entry.500354492`       |
| Body Type                   | `entry.352807454`       |
| Body Radius (km)            | `entry.311252220`       |
| Body Mass (Earth masses)    | `entry.1550981279`      |
| Ring Name                   | `entry.1805555353`      |
| Ring Type                   | `entry.1045222536`      |
| Inner Radius (km)           | `entry.1741677072`      |
| Outer Radius (km)           | `entry.1379006716`      |
| Width (km)                  | `entry.1306863839`      |
| Estimated Period (seconds)  | `entry.467530390`       |
| Observed Period (seconds)   | `entry.1394317518`      |

Radii/width are converted from the meters stored internally to kilometers; periods are already in
seconds. Body Radius/Type/Mass are only available when submitting live from the results dialog
(sourced from the Spansh dump via `RingInfo`) - resubmitting an older history row that predates
these columns sends an empty Body Radius, since it isn't persisted to the CSV.

### Detecting already-submitted measurements

At startup, `CanonnClient.GetSubmittedMeasurementsAsync` downloads Canonn's published TSV of
submitted measurements and caches it in memory. `CanonnMatcher.IsSubmitted` treats a local
measurement as already submitted if a TSV row matches on commander name, system name, ring name,
and inner/outer radius and observed period within a small tolerance (to absorb rounding). This is
a fallback on top of the locally tracked `submitted` flag — the TSV can lag behind a submission
that just happened, so the local flag is what immediately flips a row to "already submitted" after
a successful send.

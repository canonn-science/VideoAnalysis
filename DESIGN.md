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
1. **Track.** The video is split into chunks and stars are tracked frame-to-frame within each chunk. For long estimated periods, per-frame motion is tiny, so the tracker skips real frames between optical-flow samples (up to a 12x stride) rather than processing every one — the stride is sized from the ring's Kepler estimate so the pixel shift between sampled frames stays roughly constant regardless of period length.
2. **Solve per chunk.** Each chunk is fit independently by `HorizontalRotationSolver` against a fixed vertical roll axis (not a fitted in-frame center, since the horizon framing keeps the axis effectively fixed). Each chunk's solved period seeds the next chunk's solve. Chunks with fewer than 20 usable tracks, or a non-finite period, are discarded.
3. **Combine.** The observed period is the median across all chunks that produced a usable fit.
4. **Confidence.** A heuristic, not a statistical guarantee — there's no ground truth at runtime. With 2+ chunks, it's based on the coefficient of variation across chunks' periods (they're independent measurements of one true rate, since Elite's rings rotate as a rigid disk). With only one usable chunk, it's based on agreement among that chunk's own tracked stars. Either way, a 5% relative spread maps to 0% confidence, scaling up to 100% at zero spread.

If no stars can be tracked at all, or no chunk produces a reliable fit, the user gets an explanatory error asking for a longer or clearer recording instead of a result.

## Results dialog

When processing finishes, a dialog shows: system/body/ring name, estimated vs. observed period, percent difference between them, confidence percentage, median roll angle (how far off level the horizon tracked), and how many of the available recording segments were usable. Nothing is written to disk until the user clicks **Save to History** — cancelling discards the result.

## Measurement log

Saved measurements are appended as a row to a local CSV file with these columns (order matters — `MeasurementRecord` keeps them in this exact order):

```
Timestamp
System Name
id64
x
y
z
Ring Name
innerRadius
outerRadius
Width
estimated rotation
observed rotation
video filename
```

A "Measurement History" tab lists the logged rows and has a button to reveal the CSV file on disk.

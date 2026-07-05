# Rotation Analysis Lab

Rotation Analysis Lab is a Windows desktop app for **Elite Dangerous** explorers doing ring-rotation citizen science with [Canonn](https://canonn.science). It estimates how fast a planetary ring or belt is rotating using orbital mechanics, tells you how long a video you need to record to confirm it, and then measures the actual rotation period from that video by tracking the apparent motion of background stars.

## Who it's for

Anyone recording ring-rotation footage for Canonn, or processing footage that's already been recorded, to turn a video into a measured rotation period and a logged data point.

## How it works

1. **Find a system.** Type a system name (3+ characters) into the search box. The app queries the [Spansh](https://spansh.co.uk) systems API for matches as you type and lets you pick one.
2. **Browse its rings.** The app fetches the full body dump for that system from Spansh and lists every ring and belt it finds, each with:
   - an estimated rotation period, computed from Kepler's third law using a nominal radius between the ring's inner and outer edge, and
   - a suggested recording duration (roughly the estimated period divided by 36, rounded up to the nearest minute).
3. **Record the video in-game.** For the ring you want to measure, park on the ring surface in free camera, face the horizon (not straight up at the rotation center), keep the horizon roughly centered with open starfield above it, avoid zooming, and record for at least the suggested duration.
4. **Upload the recording.** Click "Select Video…" on that ring's row (or drag and drop the file) to hand the video to the app.
5. **Get the result.** The app tracks stars across the frame as the disk you're standing on rotates and solves for the observed rotation period. A results dialog shows the estimated vs. observed period, the percent difference, and a confidence score. Saving the result appends a row to a local CSV log (system, coordinates, ring geometry, estimated/observed periods, video filename), viewable from the "Measurement History" tab, which also has a button to reveal the CSV file on disk.

## Download

[⬇ Download latest release (Windows, self-contained, no .NET install required)](https://github.com/canonn-science/RotationAnalysis/releases/latest)

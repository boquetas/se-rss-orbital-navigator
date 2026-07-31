# Changelog

## Unreleased
- Added automatic planetary/deep-space navigation mode detection.
- Added `NavigationMode=Planetary` and `NavigationMode=DeepSpace` manual overrides.
- Added effective navigation mode labels to dashboard, text, and diagnostics output.
- Deep-space mode now marks ship position unknown and suppresses unverified jump-window alerts.

## 0.5.0
- Added physical Button Panel route controls for cycling source and destination bodies.
- Added four-button `[RSSNAV MULTI]` control-panel support.
- Added dashboard route-control hints and improved header spacing for LCD readability.
- Added a responsive sprite dashboard for wide cockpit surfaces and square LCDs.
- Added per-element status colors, range utilization and jump-drive charge bars.
- Added compact jump-window, relative-motion and closest-approach summaries.
- Added `DisplayMode=Text` as a fallback to the detailed legacy report.
- `FontSize` now adjusts dashboard typography independently of the responsive card layout.

## 0.4.0
- Added RSS orbit-zone radius and physical body radius data from the supplied `Config.xml`.
- Jump windows now use estimated required travel distance instead of center-to-center distance alone.
- Added automatic source departure offset detection from the nearest plausible planet voxel.
- Added target arrival modes and configurable safety margin inside the target orbit-zone edge.
- LCD now shows center distance, required jump, source offset, target allowance and minimum required jump.
- Added whole-LCD text color alerts for normal, approaching, open, closing and error states.
- Added optional Sound Block alert on transition into an open and usable jump window.
- Added sound tag, cooldown, startup and API-validity settings.

## 0.3.1
- Fixed orbital phase synchronization by using `Session.GameDateTime` instead of `ElapsedPlayTime`.
- Added configurable `ModelEpoch`; default is `2081-01-01T00:00:00`.
- LCD now displays the model epoch and clock source.
- Retains automatic jump-system detection from 0.3.0.

## 0.3.0
- Automatic Jump Drive detection through the grid jump system.
- Manual and disabled jump-range modes.

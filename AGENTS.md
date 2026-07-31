# AGENTS.md

## Project

Space Engineers Session Mod that reconstructs logical Real Solar Systems (RSS) orbital positions for the Trithorne Cluster and presents navigation/jump-window information on LCD surfaces.

## Source layout

All game scripts must remain under:

`Data/Scripts/RSSOrbitalNavigator/`

`RssOrbitalNavigatorSession` is split across partial class files. Space Engineers compiles every `.cs` file in this directory into the same mod assembly.

## Compatibility constraints

- Keep syntax compatible with the C# version supported by Space Engineers mod scripts.
- Avoid external NuGet dependencies.
- Use the Space Engineers ModAPI, not Programmable Block APIs.
- Preserve the single `[MySessionComponentDescriptor]` attribute on the core partial class.
- The component runs server-side and updates roughly every 300 simulation frames.

## RSS model assumptions

- Do not calculate orbital distance from physical planet voxel coordinates or ordinary GPS coordinates. RSS stores/moves proxy representations and the physical coordinates are not the logical orbital coordinates.
- Orbital phase uses `MyAPIGateway.Session.GameDateTime - ModelEpoch` plus `TimeOffsetSeconds`.
- The current body catalog is derived from the Trithorne Cluster `Config.xml` supplied during development.
- `Nyph-Ea` is currently an approximation because the RSS sibling-body hierarchy needs dedicated handling.
- Jump geometry is an estimate and assumes favorable radial alignment between source and target.

## Jump-window behavior

- `JumpRangeMode=Auto` must use the grid jump system's reported maximum range.
- Static grids must report zero usable jump range.
- Source departure allowance and target arrival allowance reduce the center-to-center distance to an estimated required jump distance.
- A window is open when estimated required distance is within current usable jump range.

## Alerts

- Text surfaces currently use one font color for the entire LCD.
- Yellow means an approaching window, green means open, orange means open but receding, red means error.
- Sound Blocks are matched by `SoundBlockTag` on the same construct.
- Sound alerts should trigger on transition into a usable window, respect cooldown, and avoid repeated playback every update.

## Validation

Before changing orbital math or timing:

1. Compare LCD center distance with the RSS HUD for the same bodies.
2. Test a static grid with no Jump Drives.
3. Test a mobile grid with charged and uncharged Jump Drives.
4. Test `SourceRadiusMode=Auto` near the configured source planet.
5. Test the visual state transitions and a short non-looping Sound Block alert.
6. Review the Space Engineers log for `[RSS Orbital Navigator]` errors.

Do not claim successful compilation unless tested against the installed Space Engineers/Torch assemblies.

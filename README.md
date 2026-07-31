# RSS Orbital Navigator 0.5.0

Session mod for Space Engineers worlds using the Trithorne Cluster RSS configuration.

## Installation

Place the mod so the script path is:

`RSSOrbitalNavigator/Data/Scripts/RSSOrbitalNavigator/*.cs`

Include `[RSSNAV]` in the LCD, cockpit or console block name. Copy `CustomData.example.ini` into its Custom Data.

## Zone-aware jump window

Version 0.4.0 no longer compares Jump Drive range only with planet center-to-center distance.

The estimated required jump is:

`center distance - source departure offset - target arrival allowance`

Defaults:

- `NavigationMode=Auto`: uses planetary behavior near the configured source body and automatically falls back to deep-space behavior when no plausible source voxel is nearby.
- `SourceRadiusMode=Auto`: finds the nearest large planet voxel and measures the LCD/grid position from its center.
- `TargetArrivalMode=OrbitZone`: targets a point inside the target body's RSS orbit zone.
- `TargetSafetyMarginKm=25`: keeps the target point 25 km inside the zone edge.

This is a navigation estimate. It assumes a favorable departure direction from the source body toward the target. Obstacles, gravity restrictions and the final RSS transition still need to be checked in game.

### NavigationMode

- `Auto`: detects whether the grid is near a plausible source planet. Near the planet it uses the selected `SourceRadiusMode`; away from planets it uses zero source allowance without reporting a source-voxel failure.
- `Planetary`: forces source-planet behavior. With `SourceRadiusMode=Auto`, failure to find the source voxel remains a warning and uses zero source allowance.
- `DeepSpace`: skips source voxel detection and uses zero source allowance.

The dashboard and text display show both the configured mode and the effective mode. For example, `AUTO DEEP SPACE` means automatic detection selected deep-space behavior.
Because RSS does not expose a logical ship position here, deep-space mode marks the ship position as unknown, suppresses jump-window predictions and sound alerts, and labels the body-to-body values as reference-only.

### SourceRadiusMode

- `Auto`: current distance from the nearest plausible planet voxel.
- `Manual`: uses `SourceDepartureRadiusKm`.
- `Center`: uses zero source allowance; conservative.
- `OrbitZone`: assumes departure from the edge of the source orbit zone; optimistic.

### TargetArrivalMode

- `OrbitZone`: uses target orbit-zone radius minus `TargetSafetyMarginKm`.
- `Manual`: uses `TargetArrivalRadiusKm`.
- `Surface`: target physical radius plus the safety margin.
- `Center`: no target allowance; most conservative.

## Jump range

- `JumpRangeMode=Auto`: uses `IMyGridJumpDriveSystem.GetMaxJumpDistance()` on the LCD's construct.
- `JumpRangeMode=Manual`: uses `JumpRangeKm`.
- `JumpRangeMode=Off`: disables jump-window calculations.

## LCD display

`DisplayMode=Dashboard` uses a responsive sprite dashboard with a dark navigation-console layout. It adapts between wide cockpit screens and square LCDs and shows the route, estimated jump, available range, jump-window timing, relative motion, drive readiness and charge.

Set `DisplayMode=Text` to use the detailed legacy text report. Dashboard mode scales automatically to the surface dimensions, and `FontSize` can reduce its typography without changing the card layout. The default and dashboard maximum is `0.55`; cockpit surfaces may work better around `0.35` to `0.45`. Text mode continues to accept larger values.

### Physical route controls

Route selection can be changed without editing Custom Data. Add one Button Panel for each action and include the following tag in its name:

- `[RSSNAV SRC NEXT]` or `[RSSNAV SRC PREV]`
- `[RSSNAV DST NEXT]` or `[RSSNAV DST PREV]`
- `[RSSNAV RESET]`
- `[RSSNAV MULTI]` for one four-button panel

Any button press on a tagged Button Panel performs that action. Controls affect all `[RSSNAV]` panels on the same construct. `SourceBody` and `TargetBody` remain the startup and reset defaults.

For `[RSSNAV MULTI]`, buttons are mapped as follows: button 1 source previous, button 2 source next, button 3 destination previous, and button 4 destination next. The game reports these button indexes as 0 through 3 internally. A reset button remains a separate `[RSSNAV RESET]` panel.

Dashboard status badges use these colors:

- cyan/white: normal monitoring;
- yellow: window opens within `AlertLeadMinutes`;
- green: jump window open;
- orange: window open but receding/closing;
- red: configuration/error state.

Colors accept `R,G,B` or `#RRGGBB`. Set `ColorAlertsEnabled=false` to keep a neutral cyan status accent.

## Sound alert

1. Add a Sound Block to the same construct as the LCD.
2. Put `[RSSNAV ALERT]` in its name, for example `Bridge Sound Block [RSSNAV ALERT]`.
3. Select a short, non-looping sound in the block terminal.
4. Leave the block enabled and functional.

The mod calls `Play()` once when the system transitions into an open, usable jump window. `SoundOnStartup=false` prevents a sound immediately after loading a save that is already inside the window. `SoundCooldownSeconds` protects against repeated threshold crossings.

## Orbital clock

The model uses `MyAPIGateway.Session.GameDateTime - ModelEpoch`. The tested Trithorne world uses `2081-01-01T00:00:00`.

## Diagnostics

`ShowDiagnostics=false` keeps the LCD compact. Set it to `true` to show model time, epoch and the favorable-alignment warning on the panel.

## Repository layout

The session component is split across partial class files under `Data/Scripts/RSSOrbitalNavigator` so each source file remains manageable. Space Engineers compiles all `.cs` files in that script directory together.

`SteamWorkshopDescription.txt` contains the formatted Workshop page description and should be updated with user-facing feature changes.

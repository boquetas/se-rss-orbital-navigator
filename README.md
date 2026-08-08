# RSS Orbital Navigator 0.10.0

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
- `ShipForecastMinutes=30`: short-horizon ship trajectory forecast used after two RSS position samples are available.
- `PanelUpdateRadiusKm=0.2`: only updates the panel when a player is within this distance; the default is 200 m and the last displayed content is retained while distant.
- `TargetArrivalMode=OrbitZone`: targets a point inside the target body's RSS orbit zone.
- `TargetSafetyMarginKm=25`: keeps the target point 25 km inside the zone edge.

This is a navigation estimate. It assumes a favorable departure direction from the source body toward the target. Obstacles, gravity restrictions and the final RSS transition still need to be checked in game.

### NavigationMode

- `Auto`: detects whether the grid is near a plausible source planet. Near the planet it uses the selected `SourceRadiusMode`; away from planets it uses zero source allowance without reporting a source-voxel failure.
- `Planetary`: forces source-planet behavior. With `SourceRadiusMode=Auto`, failure to find the source voxel remains a warning and uses zero source allowance.
- `DeepSpace`: skips source voxel detection and uses zero source allowance.

The dashboard and text display show both the configured mode and the effective mode. For example, `AUTO DEEP SPACE` means automatic detection selected deep-space behavior.
When the RSS logical-position API is available, deep-space mode uses the ship's converted logical proxy position and performs a current ship-to-target range check. After two samples, it estimates relative required-distance movement over `ShipForecastMinutes`. If the API or target body is unavailable, it shows an amber `POSITION UNKNOWN` state, suppresses predictions and sound alerts, and labels body-to-body values as reference-only.
In RSS-position deep-space mode, the display shows `SHIP > Target`; `SourceBody` is retained as configuration context but is not the physical departure point.

This provides two distinct navigation views:

- Planetary mode: `Source > Target`, using the logical planet-to-planet route and configured departure/arrival allowances.
- Deep-space RSS-position mode: `SHIP > Target`, using the current logical ship-to-target distance and Jump Drive reachability.

After two RSS position samples, deep-space mode can estimate short-horizon approach and opening/closing behavior using `ShipForecastMinutes`. When a direct jump is out of range, the dashboard reports the number of full-range jumps required and the distance threshold for reducing that count.

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

`DisplayMode=Dashboard` uses a responsive sprite dashboard with a dark navigation-console layout. It adapts between wide cockpit screens and square LCDs and shows the route, estimated jump, available range, jump-window timing and duration, relative motion, drive readiness and charge. Forecast-bound window durations use `>` to distinguish a lower bound from an exact close time.

Set `DisplayMode=Text` to use the detailed legacy text report. Dashboard mode scales automatically to the surface dimensions, and `FontSize` can reduce its typography without changing the card layout. The default and dashboard maximum is `0.55`; cockpit surfaces may work better around `0.35` to `0.45`. Text mode continues to accept larger values.

`PredictionHours` accepts values from `0.25` through `60000`. Long planetary forecasts retain a 60-second scan for the first 48 hours and use bounded coarse sampling beyond that period.

### Physical route controls

Route selection can be changed without editing Custom Data. Add one Button Panel for each action and include the following tag in its name:

- `[RSSNAV SRC NEXT]` or `[RSSNAV SRC PREV]`
- `[RSSNAV DST NEXT]` or `[RSSNAV DST PREV]`
- `[RSSNAV RESET]`
- `[RSSNAV MULTI]` for one four-button panel

Any button press on a tagged Button Panel performs that action. Controls affect all `[RSSNAV]` panels on the same construct. `SourceBody` and `TargetBody` remain the startup and reset defaults.

To control only one route station, set the same `RouteGroup` in the LCD and in the Button Panel Custom Data:

```ini
; LCD Custom Data
[RSSNAV]
RouteGroup=Bridge
SourceBody=Luburn
TargetBody=Thalion
```

```ini
; Button Panel Custom Data
[RSSNAV]
RouteGroup=Bridge
```

The button then changes only LCDs in the same construct with `RouteGroup=Bridge`. A button without `RouteGroup` keeps the legacy behavior and controls all RSSNAV panels on that construct.

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

## Release Process

Releases use the `develop` to `main` workflow:

1. Update the version in the first heading of `README.md`.
2. Move the completed entries in `CHANGELOG.md` from `Unreleased` into the new version section.
3. Update `SteamWorkshopDescription.txt`, `CustomData.example.ini`, and README documentation as needed.
4. Run `git diff --check` and validate that exactly one session-component descriptor exists.
5. Commit and push `develop`.
6. Merge `develop` into `main` and push `main`.
7. Confirm the Release workflow creates `v<version>` and `RSSOrbitalNavigator-<version>.zip`.

The `develop` workflow updates the prerelease `develop-latest`. The `main` workflow reads the version from `README.md` and refuses to reuse an existing release tag.

## Repository layout

The session component is split across partial class files under `Data/Scripts/RSSOrbitalNavigator` so each source file remains manageable. Space Engineers compiles all `.cs` files in that script directory together.

`SteamWorkshopDescription.txt` contains the formatted Workshop page description and should be updated with user-facing feature changes.

## Local Workshop Publishing

The existing Workshop item is `3774648307`. After preparing and testing a release locally, publish it with SteamCMD:

```bash
STEAMCMD=/path/to/steamcmd ./scripts/publish-workshop.sh
```

The script copies only `Data/` into a temporary Workshop content directory and uses `SteamWorkshopDescription.txt` as the item description. It asks for Steam credentials and confirmation at runtime; credentials are never stored in the repository. Use `--dry-run` to inspect the generated Workshop VDF without uploading:

```bash
./scripts/publish-workshop.sh --dry-run
```

For the first upload on an account, Steam Guard may require an interactive code. The script updates the configured existing item and does not create a new Workshop item.

Install SteamCMD separately in WSL, or point `STEAMCMD` at an existing executable. The script passes only the account name; SteamCMD itself requests the password and Steam Guard code interactively.

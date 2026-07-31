using System;
using System.Globalization;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRageMath;

using TextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;

namespace Boquetas.RssOrbitalNavigator
{
    public sealed partial class RssOrbitalNavigatorSession
    {
        private static readonly Color DashboardBackground = new Color(6, 14, 21);
        private static readonly Color DashboardPanel = new Color(13, 29, 40);
        private static readonly Color DashboardPanelBright = new Color(18, 39, 52);
        private static readonly Color DashboardText = new Color(224, 238, 246);
        private static readonly Color DashboardMuted = new Color(115, 148, 165);
        private static readonly Color DashboardAccent = new Color(64, 210, 230);
        private static readonly Color DashboardDanger = new Color(255, 72, 72);

        private static void WritePanel(TextSurface surface, PanelConfig config, Snapshot snapshot, AlertResult alert)
        {
            if (config.DisplayMode == PanelDisplayMode.Text)
            {
                WriteTextPanel(surface, config, snapshot, alert);
                return;
            }

            try
            {
                WriteDashboard(surface, config, snapshot, alert);
            }
            catch (Exception exception)
            {
                Log("Dashboard rendering failed; using text mode: " + exception.Message);
                WriteTextPanel(surface, config, snapshot, alert);
            }
        }

        private static void WriteDashboard(TextSurface surface, PanelConfig config, Snapshot snapshot, AlertResult alert)
        {
            surface.ContentType = ContentType.SCRIPT;
            surface.Script = string.Empty;
            surface.ScriptBackgroundColor = DashboardBackground;

            Vector2 size = surface.SurfaceSize;
            Vector2 origin = (surface.TextureSize - size) * 0.5f;
            float aspect = size.Y > 0f ? size.X / size.Y : 1f;

            using (MySpriteDrawFrame frame = surface.DrawFrame())
            {
                AddRectangle(frame, origin + size * 0.5f, size, DashboardBackground);

                if (!snapshot.IsValid)
                {
                    DrawErrorDashboard(frame, origin, size, config, snapshot, alert);
                    return;
                }

                if (aspect >= 1.35f)
                    DrawWideDashboard(frame, origin, size, config, snapshot, alert);
                else
                    DrawSquareDashboard(frame, origin, size, config, snapshot, alert);
            }
        }

        private static void DrawWideDashboard(MySpriteDrawFrame frame, Vector2 origin, Vector2 size,
            PanelConfig config, Snapshot snapshot, AlertResult alert)
        {
            float unit = Math.Min(size.X / 512f, size.Y / 288f);
            float fontUnit = GetDashboardFontUnit(unit, config);
            float margin = 14f * unit;
            float headerHeight = 82f * unit;
            float gap = 9f * unit;
            Color accent = config.ColorAlertsEnabled ? alert.FontColor : DashboardAccent;
            Vector2 topLeft = origin + new Vector2(margin, margin);
            float contentWidth = size.X - margin * 2f;

            AddText(frame, Shorten(config.Title, 28), topLeft, 0.62f * fontUnit, DashboardMuted, TextAlignment.LEFT);
            AddText(frame, FormatRouteLabel(snapshot, 8),
                topLeft + new Vector2(0f, 22f * unit), 1.02f * fontUnit, DashboardText, TextAlignment.LEFT);
            AddText(frame, FormatNavigationMode(snapshot.Geometry) + "   " + FormatControlHint(snapshot),
                topLeft + new Vector2(0f, 48f * unit),
                0.46f * fontUnit, DashboardMuted, TextAlignment.LEFT);
            AddBadge(frame, FormatBadgeText(alert.Level),
                origin + new Vector2(size.X - margin, margin + 16f * unit), accent, unit, fontUnit);
            AddRectangle(frame, origin + new Vector2(size.X * 0.5f, margin + headerHeight),
                new Vector2(contentWidth, 2f * unit), accent);

            float contentTop = margin + headerHeight + gap;
            float contentHeight = size.Y - contentTop - margin;
            float leftWidth = contentWidth * 0.56f;
            float rightWidth = contentWidth - leftWidth - gap;
            Vector2 left = origin + new Vector2(margin, contentTop);
            Vector2 right = left + new Vector2(leftWidth + gap, 0f);

            AddCard(frame, left, new Vector2(leftWidth, contentHeight), unit);
            AddText(frame, snapshot.Geometry.UsesLogicalShipPosition ? "CURRENT SHIP-TO-TARGET"
                    : (snapshot.Geometry.IsShipPositionKnown ? "ESTIMATED JUMP" : "BODY ROUTE REFERENCE"),
                left + new Vector2(12f, 10f) * unit,
                0.55f * fontUnit, DashboardMuted, TextAlignment.LEFT);
            AddText(frame, FormatDashboardDistance(snapshot.RequiredJumpMeters), left + new Vector2(12f, 31f) * unit,
                1.28f * fontUnit, DashboardText, TextAlignment.LEFT);

            double range = snapshot.JumpInfo.RangeMeters;
            double marginMeters = range - snapshot.RequiredJumpMeters;
            string rangeLabel = range > 0.0 ? "RANGE " + FormatDashboardDistance(range) : "RANGE UNAVAILABLE";
            AddText(frame, rangeLabel, left + new Vector2(leftWidth / unit - 12f, 84f) * unit,
                0.48f * fontUnit, DashboardMuted, TextAlignment.RIGHT);
            DrawProgressBar(frame, left + new Vector2(12f, 70f) * unit,
                leftWidth - 24f * unit, 8f * unit,
                range > 0.0 ? snapshot.RequiredJumpMeters / range : 0.0,
                !snapshot.Geometry.IsShipPositionKnown ? DashboardMuted
                    : (range > 0.0 && marginMeters >= 0.0 ? accent : DashboardDanger));
            AddText(frame, range > 0.0
                    ? (snapshot.Geometry.IsShipPositionKnown ? FormatMargin(marginMeters) : "REFERENCE ONLY")
                    : "NO USABLE JUMP RANGE",
                left + new Vector2(12f, 84f) * unit, 0.48f * fontUnit,
                !snapshot.Geometry.IsShipPositionKnown ? DashboardMuted
                    : (range > 0.0 && marginMeters >= 0.0 ? accent : DashboardDanger), TextAlignment.LEFT);

            float detailTop = 111f * unit;
            AddMetric(frame, left + new Vector2(12f, detailTop / unit),
                snapshot.Geometry.UsesLogicalShipPosition ? "SHIP DISTANCE" : "CENTER DISTANCE",
                FormatDashboardDistance(snapshot.DistanceMeters), unit, fontUnit);
            AddMetric(frame, left + new Vector2(leftWidth / unit * 0.52f, detailTop / unit),
                snapshot.Geometry.UsesLogicalShipPosition ? "SHIP TRAJECTORY" : "RADIAL km/min",
                snapshot.Geometry.UsesLogicalShipPosition
                    ? (snapshot.Geometry.HasShipTrajectory ? "ESTIMATED" : "NOT MODELED")
                    : FormatMotion(snapshot), unit, fontUnit);

            float lowerTop = Math.Min(contentHeight - 48f * unit, 167f * unit);
            AddText(frame, "NEXT CLOSEST", left + new Vector2(12f, lowerTop / unit) * unit,
                0.45f * fontUnit, DashboardMuted, TextAlignment.LEFT);
            AddText(frame, snapshot.Closest.Found
                    ? FormatDashboardDistance(snapshot.Closest.RequiredJumpMeters) : "NO FORECAST",
                left + new Vector2(12f, lowerTop / unit + 17f) * unit,
                0.68f * fontUnit, DashboardText, TextAlignment.LEFT);
            AddText(frame, snapshot.Closest.Found
                    ? "IN " + FormatDashboardDuration(snapshot.Closest.SecondsFromNow) : string.Empty,
                left + new Vector2(leftWidth / unit - 12f, lowerTop / unit + 20f) * unit,
                0.48f * fontUnit, DashboardMuted, TextAlignment.RIGHT);

            AddCard(frame, right, new Vector2(rightWidth, contentHeight), unit);
            DrawWindowSummary(frame, right, rightWidth, snapshot, accent, unit, fontUnit);
            DrawDriveSummary(frame, right + new Vector2(0f, contentHeight * 0.51f), rightWidth,
                contentHeight * 0.49f, snapshot, accent, unit, fontUnit);

            AddText(frame, "UPDATED " + snapshot.SampleTime.ToString("HH:mm:ss"),
                origin + new Vector2(size.X - margin, size.Y - margin - 4f * unit),
                0.4f * fontUnit, DashboardMuted, TextAlignment.RIGHT);
            if (config.ShowDiagnostics)
                AddText(frame, FormatDiagnostics(config, snapshot),
                    origin + new Vector2(margin, size.Y - margin - 4f * unit),
                    0.34f * fontUnit, DashboardMuted, TextAlignment.LEFT);
        }

        private static void DrawSquareDashboard(MySpriteDrawFrame frame, Vector2 origin, Vector2 size,
            PanelConfig config, Snapshot snapshot, AlertResult alert)
        {
            float unit = Math.Min(size.X / 512f, size.Y / 512f);
            float fontUnit = GetDashboardFontUnit(unit, config);
            float margin = 16f * unit;
            float width = size.X - margin * 2f;
            Color accent = config.ColorAlertsEnabled ? alert.FontColor : DashboardAccent;
            Vector2 cursor = origin + new Vector2(margin, margin);

            AddText(frame, Shorten(config.Title, 30), cursor, 0.58f * fontUnit, DashboardMuted, TextAlignment.LEFT);
            AddText(frame, FormatRouteLabel(snapshot, 12),
                cursor + new Vector2(0f, 23f * unit), 0.92f * fontUnit, DashboardText, TextAlignment.LEFT);
            AddText(frame, FormatNavigationMode(snapshot.Geometry) + "   " + FormatControlHint(snapshot),
                cursor + new Vector2(0f, 50f * unit),
                0.45f * fontUnit, DashboardMuted, TextAlignment.LEFT);
            AddBadge(frame, FormatBadgeText(alert.Level),
                origin + new Vector2(size.X - margin, margin + 11f * unit), accent, unit, fontUnit);

            cursor.Y += 88f * unit;
            AddCard(frame, cursor, new Vector2(width, 116f * unit), unit);
            AddText(frame, snapshot.Geometry.UsesLogicalShipPosition ? "CURRENT SHIP-TO-TARGET"
                    : (snapshot.Geometry.IsShipPositionKnown ? "ESTIMATED JUMP" : "BODY ROUTE REFERENCE"),
                cursor + new Vector2(12f, 11f) * unit,
                0.5f * fontUnit, DashboardMuted, TextAlignment.LEFT);
            AddText(frame, FormatDashboardDistance(snapshot.RequiredJumpMeters), cursor + new Vector2(12f, 34f) * unit,
                1.32f * fontUnit, DashboardText, TextAlignment.LEFT);

            double range = snapshot.JumpInfo.RangeMeters;
            double marginMeters = range - snapshot.RequiredJumpMeters;
            DrawProgressBar(frame, cursor + new Vector2(12f, 81f) * unit, width - 24f * unit, 8f * unit,
                range > 0.0 ? snapshot.RequiredJumpMeters / range : 0.0,
                !snapshot.Geometry.IsShipPositionKnown ? DashboardMuted
                    : (range > 0.0 && marginMeters >= 0.0 ? accent : DashboardDanger));
            AddText(frame, range > 0.0
                    ? (snapshot.Geometry.IsShipPositionKnown ? FormatMargin(marginMeters) : "REFERENCE ONLY")
                    : "NO USABLE JUMP RANGE",
                cursor + new Vector2(12f, 96f) * unit, 0.46f * fontUnit,
                !snapshot.Geometry.IsShipPositionKnown ? DashboardMuted
                    : (range > 0.0 && marginMeters >= 0.0 ? accent : DashboardDanger), TextAlignment.LEFT);
            AddText(frame, range > 0.0 ? "RANGE " + FormatDashboardDistance(range) : string.Empty,
                cursor + new Vector2(width / unit - 12f, 96f) * unit,
                0.46f * fontUnit, DashboardMuted, TextAlignment.RIGHT);

            cursor.Y += 126f * unit;
            AddCard(frame, cursor, new Vector2(width, 66f * unit), unit);
            AddMetric(frame, cursor + new Vector2(12f, 10f) * unit,
                snapshot.Geometry.UsesLogicalShipPosition ? "SHIP DISTANCE" : "CENTER DISTANCE",
                FormatDashboardDistance(snapshot.DistanceMeters), unit, fontUnit);
            AddMetric(frame, cursor + new Vector2(width / unit * 0.52f, 10f) * unit,
                snapshot.Geometry.UsesLogicalShipPosition ? "SHIP TRAJECTORY" : "RADIAL km/min",
                snapshot.Geometry.UsesLogicalShipPosition
                    ? (snapshot.Geometry.HasShipTrajectory ? "ESTIMATED" : "NOT MODELED")
                    : FormatMotion(snapshot), unit, fontUnit);

            cursor.Y += 76f * unit;
            AddCard(frame, cursor, new Vector2(width, 92f * unit), unit);
            DrawWindowSummary(frame, cursor, width, snapshot, accent, unit, fontUnit);

            cursor.Y += 102f * unit;
            AddCard(frame, cursor, new Vector2(width, 82f * unit), unit);
            DrawDriveSummary(frame, cursor, width, 82f * unit, snapshot, accent, unit, fontUnit);

            AddText(frame, "UPDATED " + snapshot.SampleTime.ToString("HH:mm:ss"),
                origin + new Vector2(size.X - margin, size.Y - margin),
                0.42f * fontUnit, DashboardMuted, TextAlignment.RIGHT);
            if (config.ShowDiagnostics)
                AddText(frame, FormatDiagnostics(config, snapshot),
                    origin + new Vector2(margin, size.Y - margin),
                    0.32f * fontUnit, DashboardMuted, TextAlignment.LEFT);
        }

        private static void DrawWindowSummary(MySpriteDrawFrame frame, Vector2 topLeft, float width,
            Snapshot snapshot, Color accent, float unit, float fontUnit)
        {
            AddText(frame, "JUMP WINDOW", topLeft + new Vector2(12f, 10f) * unit,
                0.5f * fontUnit, DashboardMuted, TextAlignment.LEFT);
            if (snapshot.Closest.Found)
                AddText(frame, "MIN " + FormatDashboardDistance(snapshot.Closest.RequiredJumpMeters),
                    topLeft + new Vector2(width / unit - 12f, 10f) * unit,
                    0.36f * fontUnit, DashboardMuted, TextAlignment.RIGHT);

            string state;
            string detail;
            Color stateColor = DashboardText;
            if (!snapshot.Geometry.IsShipPositionKnown)
            {
                state = "POSITION UNKNOWN";
                detail = "BODY ROUTE ONLY - RSS POSITION REQUIRED";
                stateColor = accent;
            }
            else if (snapshot.Geometry.UsesLogicalShipPosition)
            {
                if (snapshot.JumpWindow.IsOpenNow)
                {
                    state = "CURRENTLY REACHABLE";
                    detail = snapshot.JumpWindow.HasClose
                        ? "CLOSES IN " + FormatDashboardDuration(snapshot.JumpWindow.CloseSecondsFromNow)
                        : "CURRENT SHIP-TO-TARGET CHECK";
                    stateColor = accent;
                }
                else if (snapshot.Geometry.HasShipTrajectory && snapshot.JumpWindow.Found)
                {
                    state = "OPENS IN " + FormatDashboardDuration(snapshot.JumpWindow.OpenSecondsFromNow);
                    detail = "SHIP TRAJECTORY FORECAST";
                    stateColor = accent;
                }
                else
                {
                    state = "OUT OF RANGE";
                    detail = "CURRENT SHIP-TO-TARGET CHECK";
                    stateColor = DashboardDanger;
                }
            }
            else if (snapshot.JumpInfo.RangeMeters <= 0.0)
            {
                state = "UNAVAILABLE";
                detail = snapshot.JumpInfo.IsStaticGrid ? "STATIC GRID" : "NO USABLE RANGE";
                stateColor = DashboardDanger;
            }
            else if (!snapshot.JumpWindow.Found)
            {
                state = "NOT FOUND";
                detail = "OUTSIDE FORECAST";
            }
            else if (snapshot.JumpWindow.IsOpenNow)
            {
                state = "OPEN NOW";
                detail = snapshot.JumpWindow.HasClose
                    ? "CLOSES IN " + FormatDashboardDuration(snapshot.JumpWindow.CloseSecondsFromNow)
                    : "NO CLOSE IN FORECAST";
                stateColor = accent;
            }
            else
            {
                state = "OPENS IN " + FormatDashboardDuration(snapshot.JumpWindow.OpenSecondsFromNow);
                detail = snapshot.JumpWindow.HasClose
                    ? "DURATION " + FormatDashboardDuration(snapshot.JumpWindow.CloseSecondsFromNow
                        - snapshot.JumpWindow.OpenSecondsFromNow)
                    : "OPENING FORECAST";
            }

            AddText(frame, state, topLeft + new Vector2(12f, 34f) * unit,
                0.68f * fontUnit, stateColor, TextAlignment.LEFT);
            AddText(frame, detail, topLeft + new Vector2(12f, 61f) * unit,
                0.44f * fontUnit, DashboardMuted, TextAlignment.LEFT);
        }

        private static void DrawDriveSummary(MySpriteDrawFrame frame, Vector2 topLeft, float width, float height,
            Snapshot snapshot, Color accent, float unit, float fontUnit)
        {
            AddText(frame, "JUMP SYSTEM", topLeft + new Vector2(12f, 8f) * unit,
                0.48f * fontUnit, DashboardMuted, TextAlignment.LEFT);

            string drives;
            if (snapshot.JumpInfo.Mode == JumpRangeMode.Off)
                drives = "RANGE MODE OFF";
            else if (snapshot.JumpInfo.Mode == JumpRangeMode.Manual)
                drives = "MANUAL RANGE";
            else
                drives = snapshot.JumpInfo.ReadyDrives.ToString(CultureInfo.InvariantCulture) + " / "
                    + snapshot.JumpInfo.TotalDrives.ToString(CultureInfo.InvariantCulture) + " DRIVES READY";

            AddText(frame, drives, topLeft + new Vector2(12f, 29f) * unit,
                0.5f * fontUnit, DashboardText, TextAlignment.LEFT);

            if (snapshot.JumpInfo.HasChargeData)
            {
                float barY = Math.Min(height / unit - 24f, 58f);
                DrawProgressBar(frame, topLeft + new Vector2(12f, barY) * unit,
                    width - 24f * unit, 7f * unit, snapshot.JumpInfo.ChargeRatio, accent);
                AddText(frame, (snapshot.JumpInfo.ChargeRatio * 100.0).ToString("0", CultureInfo.InvariantCulture)
                    + "% CHARGED", topLeft + new Vector2(width / unit - 12f, barY - 12f) * unit,
                    0.4f * fontUnit, DashboardMuted, TextAlignment.RIGHT);
            }

            string warning = snapshot.JumpInfo.ErrorMessage;
            if (string.IsNullOrWhiteSpace(warning))
                warning = snapshot.Geometry.Warning;
            if (!string.IsNullOrWhiteSpace(warning))
                AddText(frame, Shorten(warning, Math.Max(12, (int)(width / (7f * fontUnit)))),
                    topLeft + new Vector2(12f, height / unit - 13f) * unit,
                    0.38f * fontUnit, DashboardDanger, TextAlignment.LEFT);
        }

        private static void DrawErrorDashboard(MySpriteDrawFrame frame, Vector2 origin, Vector2 size,
            PanelConfig config, Snapshot snapshot, AlertResult alert)
        {
            float unit = Math.Min(size.X / 512f, size.Y / 512f);
            float fontUnit = GetDashboardFontUnit(unit, config);
            Vector2 center = origin + size * 0.5f;
            float width = Math.Min(size.X - 32f * unit, 460f * unit);
            Color errorColor = config.ColorAlertsEnabled ? alert.FontColor : DashboardAccent;
            AddRectangle(frame, center, new Vector2(width, 180f * unit), DashboardPanel);
            AddRectangle(frame, center - new Vector2(0f, 88f * unit),
                new Vector2(width, 4f * unit), errorColor);
            AddText(frame, Shorten(config.Title, 30), center - new Vector2(0f, 62f * unit),
                0.58f * fontUnit, DashboardMuted, TextAlignment.CENTER);
            AddText(frame, "NAVIGATION ERROR", center - new Vector2(0f, 27f * unit),
                1.0f * fontUnit, errorColor, TextAlignment.CENTER);
            AddText(frame, Shorten(snapshot.ErrorMessage ?? "Unknown error", 52), center + new Vector2(0f, 11f * unit),
                0.52f * fontUnit, DashboardText, TextAlignment.CENTER);
            AddText(frame, "CHECK [RSSNAV] CUSTOM DATA", center + new Vector2(0f, 54f * unit),
                0.46f * fontUnit, DashboardMuted, TextAlignment.CENTER);
        }

        private static void AddCard(MySpriteDrawFrame frame, Vector2 topLeft, Vector2 size, float unit)
        {
            AddRectangle(frame, topLeft + size * 0.5f, size, DashboardPanel);
            AddRectangle(frame, topLeft + new Vector2(1.5f * unit, size.Y * 0.5f),
                new Vector2(3f * unit, size.Y), DashboardPanelBright);
        }

        private static void AddMetric(MySpriteDrawFrame frame, Vector2 topLeft, string label, string value,
            float unit, float fontUnit)
        {
            AddText(frame, label, topLeft, 0.43f * fontUnit, DashboardMuted, TextAlignment.LEFT);
            AddText(frame, value, topLeft + new Vector2(0f, 19f * unit),
                0.63f * fontUnit, DashboardText, TextAlignment.LEFT);
        }

        private static void AddBadge(MySpriteDrawFrame frame, string text, Vector2 rightCenter, Color color,
            float unit, float fontUnit)
        {
            string label = text ?? "MONITORING";
            float width = Math.Max(88f * unit, 18f * unit + label.Length * 9f * fontUnit);
            AddRectangle(frame, rightCenter - new Vector2(width * 0.5f, 0f),
                new Vector2(width, 24f * unit), color);
            AddText(frame, label, rightCenter - new Vector2(width * 0.5f, 5f * unit),
                0.47f * fontUnit, DashboardBackground, TextAlignment.CENTER);
        }

        private static string FormatBadgeText(AlertLevel level)
        {
            if (level == AlertLevel.OpenReceding)
                return "CLOSING";
            if (level == AlertLevel.Open)
                return "WINDOW OPEN";
            if (level == AlertLevel.Soon)
                return "OPENS SOON";
            if (level == AlertLevel.PositionUnknown)
                return "POSITION UNKNOWN";
            if (level == AlertLevel.OutOfRange)
                return "OUT OF RANGE";
            if (level == AlertLevel.Error)
                return "ERROR";
            return "MONITORING";
        }

        private static void DrawProgressBar(MySpriteDrawFrame frame, Vector2 topLeft, float width, float height,
            double ratio, Color color)
        {
            double bounded = Math.Max(0.0, Math.Min(1.0, ratio));
            AddRectangle(frame, topLeft + new Vector2(width * 0.5f, height * 0.5f),
                new Vector2(width, height), DashboardPanelBright);
            if (bounded <= 0.0)
                return;
            float fillWidth = width * (float)bounded;
            AddRectangle(frame, topLeft + new Vector2(fillWidth * 0.5f, height * 0.5f),
                new Vector2(fillWidth, height), color);
        }

        private static void AddRectangle(MySpriteDrawFrame frame, Vector2 center, Vector2 size, Color color)
        {
            frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", center, size, color));
        }

        private static void AddText(MySpriteDrawFrame frame, string text, Vector2 position, float scale,
            Color color, TextAlignment alignment)
        {
            MySprite sprite = MySprite.CreateText(text ?? string.Empty, "Monospace", color, scale, alignment);
            sprite.Position = position;
            frame.Add(sprite);
        }

        private static float GetDashboardFontUnit(float unit, PanelConfig config)
        {
            float scale = config.FontSize / 0.55f;
            scale = Math.Max(0.45f, Math.Min(1f, scale));
            return unit * scale;
        }

        private static string FormatMargin(double marginMeters)
        {
            if (marginMeters >= 0.0)
                return FormatDashboardDistance(marginMeters) + " RESERVE";
            return FormatDashboardDistance(-marginMeters) + " SHORT";
        }

        private static string FormatMotion(Snapshot snapshot)
        {
            string direction;
            if (snapshot.Status == MotionStatus.Closing)
                direction = "CLOSE ";
            else if (snapshot.Status == MotionStatus.Receding)
                direction = "RECEDE ";
            else
                direction = "STABLE ";
            return direction
                + Math.Abs(snapshot.RadialRateKmPerMinute).ToString("0.00", CultureInfo.InvariantCulture);
        }

        private static string FormatDashboardDistance(double meters)
        {
            double kilometers = meters / 1000.0;
            if (kilometers >= 1000000.0)
                return (kilometers / 1000000.0).ToString("0.00", CultureInfo.InvariantCulture) + "M km";
            if (kilometers >= 1000.0)
                return (kilometers / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + "k km";
            return kilometers.ToString("0.0", CultureInfo.InvariantCulture) + " km";
        }

        private static string FormatDashboardDuration(double totalSeconds)
        {
            if (totalSeconds < 0.0 || double.IsNaN(totalSeconds) || double.IsInfinity(totalSeconds))
                return "n/a";
            TimeSpan duration = TimeSpan.FromSeconds(totalSeconds);
            if (duration.TotalDays >= 1.0)
                return ((int)duration.TotalDays).ToString(CultureInfo.InvariantCulture) + "d "
                    + duration.Hours.ToString(CultureInfo.InvariantCulture) + "h";
            if (duration.TotalHours >= 1.0)
                return ((int)duration.TotalHours).ToString(CultureInfo.InvariantCulture) + "h "
                    + duration.Minutes.ToString("00", CultureInfo.InvariantCulture) + "m";
            return ((int)duration.TotalMinutes).ToString(CultureInfo.InvariantCulture) + "m "
                + duration.Seconds.ToString("00", CultureInfo.InvariantCulture) + "s";
        }

        private static string FormatDiagnostics(PanelConfig config, Snapshot snapshot)
        {
            return "MODEL " + FormatDuration(snapshot.ModelSeconds) + "  |  EPOCH "
                + config.ModelEpoch.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                + "  |  NAV " + FormatNavigationMode(snapshot.Geometry);
        }

        private static string FormatNavigationMode(NavigationGeometry geometry)
        {
            if (geometry == null)
                return "NAV UNKNOWN";

            if (geometry.UsesLogicalShipPosition)
                return "RSS POSITION";

            string effective = geometry.EffectiveNavigationMode == NavigationMode.DeepSpace
                ? "DEEP SPACE" : "PLANETARY";
            if (geometry.ConfiguredNavigationMode == NavigationMode.Auto)
                return "AUTO " + effective;
            return effective;
        }

        private static string FormatRouteLabel(Snapshot snapshot, int nameLength)
        {
            if (snapshot.Geometry.UsesLogicalShipPosition)
                return "SHIP > " + Shorten(snapshot.TargetName, nameLength);
            return Shorten(snapshot.SourceName, nameLength) + " > " + Shorten(snapshot.TargetName, nameLength);
        }

        private static string FormatControlHint(Snapshot snapshot)
        {
            return snapshot.Geometry.UsesLogicalShipPosition ? "TARGET -/+" : "SRC -/+   DST -/+";
        }

        private static string Shorten(string value, int maximumLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maximumLength)
                return value ?? string.Empty;
            if (maximumLength <= 3)
                return value.Substring(0, maximumLength);
            return value.Substring(0, maximumLength - 3) + "...";
        }
    }
}

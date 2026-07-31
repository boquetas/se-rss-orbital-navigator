using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

using ModTerminalBlock = Sandbox.ModAPI.IMyTerminalBlock;
using ModTextPanel = Sandbox.ModAPI.IMyTextPanel;
using ModTextSurfaceProvider = Sandbox.ModAPI.IMyTextSurfaceProvider;
using ModJumpDrive = Sandbox.ModAPI.IMyJumpDrive;
using ModSoundBlock = SpaceEngineers.Game.ModAPI.IMySoundBlock;
using TextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;
using VoxelBase = VRage.ModAPI.IMyVoxelBase;

namespace Boquetas.RssOrbitalNavigator
{
    public sealed partial class RssOrbitalNavigatorSession
    {
        private sealed class PanelConfig
        {
            public string SourceBody = "Luburn";
            public string TargetBody = "Tropol";
            public int SurfaceIndex;
            public float FontSize = 0.55f;
            public double PredictionHours = 48.0;
            public double TimeOffsetSeconds;
            public DateTime ModelEpoch = new DateTime(2081, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
            public JumpRangeMode RangeMode = JumpRangeMode.Auto;
            public double JumpRangeKm;
            public long JumpIdentityId;
            public SourceRadiusMode SourceRadiusMode = SourceRadiusMode.Auto;
            public NavigationMode NavigationMode = NavigationMode.Auto;
            public double SourceDepartureRadiusKm;
            public TargetArrivalMode TargetArrivalMode = TargetArrivalMode.OrbitZone;
            public double TargetArrivalRadiusKm;
            public double TargetSafetyMarginKm = 25.0;
            public bool ColorAlertsEnabled = true;
            public double AlertLeadMinutes = 30.0;
            public Color NormalColor = new Color(255, 255, 255);
            public Color SoonColor = new Color(255, 220, 64);
            public Color OpenColor = new Color(64, 255, 96);
            public Color ClosingColor = new Color(255, 160, 32);
            public Color ErrorColor = new Color(255, 64, 64);
            public bool SoundAlertEnabled = true;
            public string SoundBlockTag = "[RSSNAV ALERT]";
            public double SoundCooldownSeconds = 300.0;
            public bool SoundOnStartup;
            public bool SoundRequireApiValid = true;
            public bool ShowDiagnostics;
            public string Title = "RSS ORBITAL NAVIGATOR";
            public PanelDisplayMode DisplayMode = PanelDisplayMode.Dashboard;

            public static PanelConfig Parse(string customData)
            {
                PanelConfig config = new PanelConfig();
                if (string.IsNullOrWhiteSpace(customData))
                    return config;

                bool inSection = false;
                string[] lines = customData.Replace("\r", string.Empty).Split('\n');
                foreach (string rawLine in lines)
                {
                    string line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#"))
                        continue;
                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        string section = line.Substring(1, line.Length - 2).Trim();
                        inSection = string.Equals(section, "RSSNAV", StringComparison.OrdinalIgnoreCase);
                        continue;
                    }
                    if (!inSection)
                        continue;

                    int equalsIndex = line.IndexOf('=');
                    if (equalsIndex <= 0)
                        continue;
                    string key = line.Substring(0, equalsIndex).Trim();
                    string value = line.Substring(equalsIndex + 1).Trim();

                    if (string.Equals(key, "SourceBody", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(key, "SourceGPS", StringComparison.OrdinalIgnoreCase))
                        config.SourceBody = value;
                    else if (string.Equals(key, "TargetBody", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(key, "TargetGPS", StringComparison.OrdinalIgnoreCase))
                        config.TargetBody = value;
                    else if (string.Equals(key, "Surface", StringComparison.OrdinalIgnoreCase))
                        TryParseInt(value, ref config.SurfaceIndex);
                    else if (string.Equals(key, "FontSize", StringComparison.OrdinalIgnoreCase))
                        TryParseFloat(value, ref config.FontSize);
                    else if (string.Equals(key, "PredictionHours", StringComparison.OrdinalIgnoreCase))
                        TryParseDouble(value, ref config.PredictionHours);
                    else if (string.Equals(key, "TimeOffsetSeconds", StringComparison.OrdinalIgnoreCase))
                        TryParseDouble(value, ref config.TimeOffsetSeconds);
                    else if (string.Equals(key, "ModelEpoch", StringComparison.OrdinalIgnoreCase))
                        TryParseDateTime(value, ref config.ModelEpoch);
                    else if (string.Equals(key, "JumpRangeMode", StringComparison.OrdinalIgnoreCase))
                        config.RangeMode = ParseJumpRangeMode(value);
                    else if (string.Equals(key, "JumpRangeKm", StringComparison.OrdinalIgnoreCase))
                        TryParseDouble(value, ref config.JumpRangeKm);
                    else if (string.Equals(key, "JumpIdentityId", StringComparison.OrdinalIgnoreCase))
                        TryParseLong(value, ref config.JumpIdentityId);
                    else if (string.Equals(key, "SourceRadiusMode", StringComparison.OrdinalIgnoreCase))
                        config.SourceRadiusMode = ParseSourceRadiusMode(value);
                    else if (string.Equals(key, "NavigationMode", StringComparison.OrdinalIgnoreCase))
                        config.NavigationMode = ParseNavigationMode(value);
                    else if (string.Equals(key, "SourceDepartureRadiusKm", StringComparison.OrdinalIgnoreCase))
                        TryParseDouble(value, ref config.SourceDepartureRadiusKm);
                    else if (string.Equals(key, "TargetArrivalMode", StringComparison.OrdinalIgnoreCase))
                        config.TargetArrivalMode = ParseTargetArrivalMode(value);
                    else if (string.Equals(key, "TargetArrivalRadiusKm", StringComparison.OrdinalIgnoreCase))
                        TryParseDouble(value, ref config.TargetArrivalRadiusKm);
                    else if (string.Equals(key, "TargetSafetyMarginKm", StringComparison.OrdinalIgnoreCase))
                        TryParseDouble(value, ref config.TargetSafetyMarginKm);
                    else if (string.Equals(key, "ColorAlertsEnabled", StringComparison.OrdinalIgnoreCase))
                        TryParseBool(value, ref config.ColorAlertsEnabled);
                    else if (string.Equals(key, "AlertLeadMinutes", StringComparison.OrdinalIgnoreCase))
                        TryParseDouble(value, ref config.AlertLeadMinutes);
                    else if (string.Equals(key, "NormalColor", StringComparison.OrdinalIgnoreCase))
                        TryParseColor(value, ref config.NormalColor);
                    else if (string.Equals(key, "SoonColor", StringComparison.OrdinalIgnoreCase))
                        TryParseColor(value, ref config.SoonColor);
                    else if (string.Equals(key, "OpenColor", StringComparison.OrdinalIgnoreCase))
                        TryParseColor(value, ref config.OpenColor);
                    else if (string.Equals(key, "ClosingColor", StringComparison.OrdinalIgnoreCase))
                        TryParseColor(value, ref config.ClosingColor);
                    else if (string.Equals(key, "ErrorColor", StringComparison.OrdinalIgnoreCase))
                        TryParseColor(value, ref config.ErrorColor);
                    else if (string.Equals(key, "SoundAlertEnabled", StringComparison.OrdinalIgnoreCase))
                        TryParseBool(value, ref config.SoundAlertEnabled);
                    else if (string.Equals(key, "SoundBlockTag", StringComparison.OrdinalIgnoreCase))
                        config.SoundBlockTag = value;
                    else if (string.Equals(key, "SoundCooldownSeconds", StringComparison.OrdinalIgnoreCase))
                        TryParseDouble(value, ref config.SoundCooldownSeconds);
                    else if (string.Equals(key, "SoundOnStartup", StringComparison.OrdinalIgnoreCase))
                        TryParseBool(value, ref config.SoundOnStartup);
                    else if (string.Equals(key, "SoundRequireApiValid", StringComparison.OrdinalIgnoreCase))
                        TryParseBool(value, ref config.SoundRequireApiValid);
                    else if (string.Equals(key, "ShowDiagnostics", StringComparison.OrdinalIgnoreCase))
                        TryParseBool(value, ref config.ShowDiagnostics);
                    else if (string.Equals(key, "Title", StringComparison.OrdinalIgnoreCase) && value.Length > 0)
                        config.Title = value;
                    else if (string.Equals(key, "DisplayMode", StringComparison.OrdinalIgnoreCase))
                        config.DisplayMode = ParseDisplayMode(value);
                }

                config.SurfaceIndex = Math.Max(0, config.SurfaceIndex);
                config.FontSize = Math.Max(0.1f, Math.Min(10f, config.FontSize));
                config.PredictionHours = Math.Max(0.25, Math.Min(720.0, config.PredictionHours));
                config.JumpRangeKm = Math.Max(0, config.JumpRangeKm);
                config.SourceDepartureRadiusKm = Math.Max(0, config.SourceDepartureRadiusKm);
                config.TargetArrivalRadiusKm = Math.Max(0, config.TargetArrivalRadiusKm);
                config.TargetSafetyMarginKm = Math.Max(0, config.TargetSafetyMarginKm);
                config.AlertLeadMinutes = Math.Max(0, Math.Min(1440.0, config.AlertLeadMinutes));
                config.SoundCooldownSeconds = Math.Max(0, config.SoundCooldownSeconds);
                return config;
            }

            private static JumpRangeMode ParseJumpRangeMode(string value)
            {
                if (string.Equals(value, "Manual", StringComparison.OrdinalIgnoreCase))
                    return JumpRangeMode.Manual;
                if (string.Equals(value, "Off", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(value, "Disabled", StringComparison.OrdinalIgnoreCase))
                    return JumpRangeMode.Off;
                return JumpRangeMode.Auto;
            }

            private static SourceRadiusMode ParseSourceRadiusMode(string value)
            {
                if (string.Equals(value, "Manual", StringComparison.OrdinalIgnoreCase))
                    return SourceRadiusMode.Manual;
                if (string.Equals(value, "Center", StringComparison.OrdinalIgnoreCase))
                    return SourceRadiusMode.Center;
                if (string.Equals(value, "OrbitZone", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(value, "Zone", StringComparison.OrdinalIgnoreCase))
                    return SourceRadiusMode.OrbitZone;
                return SourceRadiusMode.Auto;
            }

            private static NavigationMode ParseNavigationMode(string value)
            {
                if (string.Equals(value, "Planetary", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(value, "Planet", StringComparison.OrdinalIgnoreCase))
                    return NavigationMode.Planetary;
                if (string.Equals(value, "DeepSpace", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(value, "Deep Space", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(value, "Deep", StringComparison.OrdinalIgnoreCase))
                    return NavigationMode.DeepSpace;
                return NavigationMode.Auto;
            }

            private static TargetArrivalMode ParseTargetArrivalMode(string value)
            {
                if (string.Equals(value, "Manual", StringComparison.OrdinalIgnoreCase))
                    return TargetArrivalMode.Manual;
                if (string.Equals(value, "Surface", StringComparison.OrdinalIgnoreCase))
                    return TargetArrivalMode.Surface;
                if (string.Equals(value, "Center", StringComparison.OrdinalIgnoreCase))
                    return TargetArrivalMode.Center;
                return TargetArrivalMode.OrbitZone;
            }

            private static PanelDisplayMode ParseDisplayMode(string value)
            {
                if (string.Equals(value, "Text", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(value, "Legacy", StringComparison.OrdinalIgnoreCase))
                    return PanelDisplayMode.Text;
                return PanelDisplayMode.Dashboard;
            }

            private static void TryParseInt(string value, ref int destination)
            {
                int parsed;
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                    destination = parsed;
            }

            private static void TryParseLong(string value, ref long destination)
            {
                long parsed;
                if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                    destination = parsed;
            }

            private static void TryParseFloat(string value, ref float destination)
            {
                float parsed;
                if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                    destination = parsed;
            }

            private static void TryParseDouble(string value, ref double destination)
            {
                double parsed;
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                    destination = parsed;
            }

            private static void TryParseBool(string value, ref bool destination)
            {
                bool parsed;
                if (bool.TryParse(value, out parsed))
                {
                    destination = parsed;
                    return;
                }
                if (value == "1" || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase))
                    destination = true;
                else if (value == "0" || string.Equals(value, "no", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(value, "off", StringComparison.OrdinalIgnoreCase))
                    destination = false;
            }

            private static void TryParseColor(string value, ref Color destination)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return;

                string cleaned = value.Trim();
                if (cleaned.StartsWith("#") && cleaned.Length == 7)
                {
                    int rgb;
                    if (int.TryParse(cleaned.Substring(1), NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture, out rgb))
                    {
                        destination = new Color((byte)((rgb >> 16) & 255),
                            (byte)((rgb >> 8) & 255), (byte)(rgb & 255));
                    }
                    return;
                }

                string[] parts = cleaned.Split(',');
                if (parts.Length != 3)
                    return;
                int red;
                int green;
                int blue;
                if (int.TryParse(parts[0].Trim(), out red)
                    && int.TryParse(parts[1].Trim(), out green)
                    && int.TryParse(parts[2].Trim(), out blue))
                {
                    destination = new Color(
                        (byte)Math.Max(0, Math.Min(255, red)),
                        (byte)Math.Max(0, Math.Min(255, green)),
                        (byte)Math.Max(0, Math.Min(255, blue)));
                }
            }

            private static void TryParseDateTime(string value, ref DateTime destination)
            {
                DateTime parsed;
                string[] formats = { "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd" };
                if (DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces, out parsed))
                    destination = DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
            }
        }
    }
}

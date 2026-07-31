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
        private int CountAlertSounds(ModTerminalBlock panelBlock, string tag)
        {
            int count = 0;
            foreach (ModTerminalBlock candidate in _terminalBlocks)
            {
                ModSoundBlock sound = candidate as ModSoundBlock;
                if (sound == null || sound.MarkedForClose || sound.Closed)
                    continue;
                if (!IsSameConstruct(candidate, panelBlock))
                    continue;
                if (!NameContains(candidate.CustomName, tag))
                    continue;
                count++;
            }
            return count;
        }

        private int PlayAlertSounds(ModTerminalBlock panelBlock, string tag)
        {
            int played = 0;
            foreach (ModTerminalBlock candidate in _terminalBlocks)
            {
                ModSoundBlock sound = candidate as ModSoundBlock;
                if (sound == null || sound.MarkedForClose || sound.Closed)
                    continue;
                if (!IsSameConstruct(candidate, panelBlock))
                    continue;
                if (!NameContains(candidate.CustomName, tag))
                    continue;
                if (!sound.Enabled || !sound.IsWorking || !sound.IsSoundSelected)
                    continue;

                try
                {
                    sound.Play();
                    played++;
                }
                catch (Exception exception)
                {
                    Log("Sound alert failed on '" + candidate.CustomName + "': " + exception.Message);
                }
            }
            return played;
        }

        private static bool IsSameConstruct(ModTerminalBlock first, ModTerminalBlock second)
        {
            try
            {
                return first.IsSameConstructAs(second);
            }
            catch
            {
                return first.CubeGrid == second.CubeGrid;
            }
        }

        private static bool NameContains(string name, string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return true;
            return name != null && name.IndexOf(tag, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static TextSurface GetSurface(ModTerminalBlock block, int surfaceIndex)
        {
            ModTextPanel panel = block as ModTextPanel;
            if (panel != null)
                return panel;

            ModTextSurfaceProvider provider = block as ModTextSurfaceProvider;
            if (provider == null || provider.SurfaceCount <= 0)
                return null;

            int safeIndex = Math.Max(0, Math.Min(surfaceIndex, provider.SurfaceCount - 1));
            return provider.GetSurface(safeIndex);
        }

        private static void WriteTextPanel(TextSurface surface, PanelConfig config, Snapshot snapshot, AlertResult alert)
        {
            surface.ContentType = ContentType.TEXT_AND_IMAGE;
            surface.Alignment = TextAlignment.LEFT;
            surface.Font = "Monospace";
            surface.FontSize = config.FontSize;
            surface.TextPadding = 2f;
            if (config.ColorAlertsEnabled)
                surface.FontColor = alert.FontColor;

            StringBuilder text = new StringBuilder(1600);
            text.AppendLine(config.Title);
            text.AppendLine(new string('=', Math.Min(30, Math.Max(8, config.Title.Length))));

            if (!snapshot.IsValid)
            {
                text.AppendLine("STATUS: ERROR");
                text.AppendLine();
                text.AppendLine(snapshot.ErrorMessage);
                text.AppendLine();
                text.AppendLine("Custom Data example:");
                text.AppendLine("[RSSNAV]");
                text.AppendLine("SourceBody=Luburn");
                text.AppendLine("TargetBody=Tropol");
                surface.WriteText(text, false);
                return;
            }

            text.Append(snapshot.SourceName).Append(" -> ").AppendLine(snapshot.TargetName);
            text.Append("Navigation mode: ").AppendLine(FormatNavigationMode(snapshot.Geometry));
            if (!snapshot.Geometry.IsShipPositionKnown)
                text.AppendLine("Position: UNKNOWN (RSS logical position unavailable)");
            text.Append("Center distance: ").AppendLine(FormatDistance(snapshot.DistanceMeters));
            text.Append(snapshot.Geometry.IsShipPositionKnown ? "Est. required:  " : "Body-route ref: ")
                .AppendLine(FormatDistance(snapshot.RequiredJumpMeters));
            text.Append("Motion: ");
            if (snapshot.Status == MotionStatus.Closing)
                text.AppendLine("CLOSING");
            else if (snapshot.Status == MotionStatus.Receding)
                text.AppendLine("RECEDING");
            else
                text.AppendLine("NEARLY STABLE");

            text.Append("Rate: ")
                .Append(snapshot.RadialRateKmPerMinute >= 0 ? "+" : string.Empty)
                .Append(snapshot.RadialRateKmPerMinute.ToString("0.00", CultureInfo.InvariantCulture))
                .AppendLine(" km/min");

            text.AppendLine();
            text.AppendLine("JUMP GEOMETRY");
            text.Append("Navigation mode: ").AppendLine(FormatNavigationMode(snapshot.Geometry));
            text.Append("Source offset: ")
                .Append(FormatDistance(snapshot.Geometry.SourceAllowanceMeters))
                .Append(" (").Append(snapshot.Geometry.SourceDescription).AppendLine(")");
            text.Append("Target arrival: ")
                .Append(FormatDistance(snapshot.Geometry.TargetAllowanceMeters))
                .Append(" (").Append(snapshot.Geometry.TargetDescription).AppendLine(")");
            if (snapshot.Geometry.TargetMode == TargetArrivalMode.OrbitZone)
            {
                text.Append("Zone safety: ")
                    .Append((snapshot.Geometry.TargetSafetyMarginMeters / 1000.0)
                        .ToString("0.0", CultureInfo.InvariantCulture))
                    .AppendLine(" km inside edge");
            }
            if (!string.IsNullOrWhiteSpace(snapshot.Geometry.Warning))
                text.Append("Warning: ").AppendLine(snapshot.Geometry.Warning);

            text.AppendLine();
            text.AppendLine("ORBITAL PREDICTION");
            if (snapshot.Closest.Found)
            {
                text.Append("Next closest: ").AppendLine(FormatDistance(snapshot.Closest.DistanceMeters));
                text.Append("Min est. jump: ").AppendLine(FormatDistance(snapshot.Closest.RequiredJumpMeters));
                text.Append("ETA: ").AppendLine(FormatDuration(snapshot.Closest.SecondsFromNow));
                text.Append("At: ").AppendLine(snapshot.SampleTime.AddSeconds(snapshot.Closest.SecondsFromNow)
                    .ToString("yyyy-MM-dd HH:mm:ss"));
            }
            else
            {
                text.AppendLine("No minimum found in forecast window.");
            }

            text.AppendLine();
            text.AppendLine("JUMP SYSTEM");
            if (snapshot.JumpInfo.Mode == JumpRangeMode.Off)
            {
                text.AppendLine("Mode: OFF");
            }
            else if (snapshot.JumpInfo.Mode == JumpRangeMode.Manual)
            {
                text.AppendLine("Mode: MANUAL");
                text.Append("Range: ").AppendLine(FormatDistance(snapshot.JumpInfo.RangeMeters));
            }
            else
            {
                text.Append("Mode: AUTO");
                if (snapshot.JumpInfo.IsStaticGrid)
                    text.Append(" (STATIC GRID)");
                text.AppendLine();
                text.Append("Drives: ").Append(snapshot.JumpInfo.TotalDrives.ToString(CultureInfo.InvariantCulture));
                if (snapshot.JumpInfo.TotalDrives > 0)
                {
                    text.Append(" total, ")
                        .Append(snapshot.JumpInfo.ReadyDrives.ToString(CultureInfo.InvariantCulture))
                        .AppendLine(" ready");
                }
                else
                {
                    text.AppendLine();
                }

                if (snapshot.JumpInfo.HasChargeData)
                {
                    text.Append("Charge: ")
                        .Append((snapshot.JumpInfo.ChargeRatio * 100.0).ToString("0.0", CultureInfo.InvariantCulture))
                        .AppendLine("%");
                }

                text.Append("Range: ").AppendLine(FormatDistance(snapshot.JumpInfo.RangeMeters));
                text.Append("API valid: ").AppendLine(snapshot.JumpInfo.IsJumpValid ? "YES" : "NO");
                if (!string.IsNullOrWhiteSpace(snapshot.JumpInfo.ErrorMessage))
                    text.Append("Warning: ").AppendLine(snapshot.JumpInfo.ErrorMessage);
            }

            if (!snapshot.Geometry.IsShipPositionKnown)
            {
                text.AppendLine("Jump window: unavailable (ship position unknown)");
            }
            else if (snapshot.JumpInfo.RangeMeters > 0)
            {
                if (!snapshot.JumpWindow.Found)
                {
                    text.AppendLine("Jump window: none in forecast");
                }
                else if (snapshot.JumpWindow.IsOpenNow)
                {
                    text.AppendLine("Jump window: OPEN NOW");
                    if (snapshot.JumpWindow.HasClose)
                        text.Append("Closes in: ").AppendLine(FormatDuration(snapshot.JumpWindow.CloseSecondsFromNow));
                }
                else
                {
                    text.Append("Opens in: ").AppendLine(FormatDuration(snapshot.JumpWindow.OpenSecondsFromNow));
                    if (snapshot.JumpWindow.HasClose)
                        text.Append("Window length: ").AppendLine(FormatDuration(
                            snapshot.JumpWindow.CloseSecondsFromNow - snapshot.JumpWindow.OpenSecondsFromNow));
                }
            }

            text.AppendLine();
            text.Append("ALERT: ").AppendLine(alert.StatusText ?? "MONITORING");
            if (config.SoundAlertEnabled)
            {
                text.Append("Sound blocks: ").Append(alert.SoundBlocksFound.ToString(CultureInfo.InvariantCulture));
                if (alert.SoundTriggered)
                    text.Append(" (PLAYED)");
                text.AppendLine();
            }

            text.AppendLine();
            text.Append("Updated: ").AppendLine(snapshot.SampleTime.ToString("HH:mm:ss"));
            if (config.ShowDiagnostics)
            {
                text.Append("Model time: ").AppendLine(FormatDuration(snapshot.ModelSeconds));
                text.Append("Epoch: ").AppendLine(config.ModelEpoch.ToString("yyyy-MM-dd HH:mm:ss"));
                text.AppendLine("Clock: Session.GameDateTime");
                text.AppendLine("Model: Config.xml orbital elements");
                text.AppendLine("Geometry assumes favorable radial alignment.");
            }

            surface.WriteText(text, false);
        }

        private static string FormatDistance(double meters)
        {
            double kilometers = meters / 1000.0;
            if (kilometers >= 1000.0)
                return kilometers.ToString("N0", CultureInfo.InvariantCulture) + " km";
            return kilometers.ToString("N1", CultureInfo.InvariantCulture) + " km";
        }

        private static string FormatDuration(double totalSeconds)
        {
            if (totalSeconds < 0 || double.IsNaN(totalSeconds) || double.IsInfinity(totalSeconds))
                return "n/a";
            TimeSpan duration = TimeSpan.FromSeconds(totalSeconds);
            if (duration.TotalDays >= 1)
                return ((int)duration.TotalDays).ToString(CultureInfo.InvariantCulture) + "d " + duration.ToString(@"hh\:mm\:ss");
            return duration.ToString(@"hh\:mm\:ss");
        }

        private static void Log(string message)
        {
            MyLog.Default.WriteLineAndConsole(LogPrefix + message);
        }
    }
}

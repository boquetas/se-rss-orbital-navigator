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
        private sealed class BodyDef
        {
            public readonly string Name;
            public readonly string ParentName;
            public readonly double SemimajorAxisMeters;
            public readonly double Eccentricity;
            public readonly double PitchDegrees;
            public readonly double RollDegrees;
            public readonly double YawDegrees;
            public readonly double PeriodSeconds;
            public readonly double PhaseOffsetRadians;
            public readonly double OrbitZoneRadiusMeters;
            public readonly double BodyRadiusMeters;

            public BodyDef(string name, string parentName, double semimajorAxisMeters, double eccentricity,
                double pitchDegrees, double rollDegrees, double yawDegrees, double periodSeconds,
                double phaseOffsetRadians, double orbitZoneRadiusMeters, double bodyRadiusMeters)
            {
                Name = name;
                ParentName = parentName;
                SemimajorAxisMeters = semimajorAxisMeters;
                Eccentricity = eccentricity;
                PitchDegrees = pitchDegrees;
                RollDegrees = rollDegrees;
                YawDegrees = yawDegrees;
                PeriodSeconds = periodSeconds;
                PhaseOffsetRadians = phaseOffsetRadians;
                OrbitZoneRadiusMeters = orbitZoneRadiusMeters;
                BodyRadiusMeters = bodyRadiusMeters;
            }
        }

        private enum MotionStatus
        {
            Closing,
            Receding,
            Stable
        }

        private struct ClosestResult
        {
            public bool Found;
            public double SecondsFromNow;
            public double DistanceMeters;
            public double RequiredJumpMeters;
        }

        private enum JumpRangeMode
        {
            Auto,
            Manual,
            Off
        }

        private enum SourceRadiusMode
        {
            Auto,
            Manual,
            Center,
            OrbitZone
        }

        private enum NavigationMode
        {
            Auto,
            Planetary,
            DeepSpace
        }

        private enum TargetArrivalMode
        {
            OrbitZone,
            Manual,
            Surface,
            Center
        }

        private enum AlertLevel
        {
            Normal,
            Soon,
            Open,
            OpenReceding,
            Error,
            PositionUnknown
        }

        private enum PanelDisplayMode
        {
            Dashboard,
            Text
        }

        private sealed class JumpInfo
        {
            public JumpRangeMode Mode;
            public long IdentityId;
            public bool IsStaticGrid;
            public int TotalDrives;
            public int FunctionalDrives;
            public int WorkingDrives;
            public int ReadyDrives;
            public bool HasChargeData;
            public double ChargeRatio;
            public double ApiRangeMeters;
            public double RangeMeters;
            public bool IsJumpValid;
            public string ErrorMessage;
        }

        private sealed class NavigationGeometry
        {
            public NavigationMode ConfiguredNavigationMode;
            public NavigationMode EffectiveNavigationMode;
            public SourceRadiusMode SourceMode;
            public TargetArrivalMode TargetMode;
            public double SourceAllowanceMeters;
            public double TargetAllowanceMeters;
            public double TargetSafetyMarginMeters;
            public bool SourceAutoDetected;
            public double SourceVoxelRadiusMeters;
            public bool IsShipPositionKnown;
            public bool UsesLogicalShipPosition;
            public bool CanForecastShipPosition;
            public double ShipToTargetDistanceMeters;
            public string SourceDescription;
            public string TargetDescription;
            public string Warning;
        }

        private struct JumpWindow
        {
            public bool Found;
            public bool IsOpenNow;
            public double OpenSecondsFromNow;
            public bool HasClose;
            public double CloseSecondsFromNow;
        }

        private sealed class Snapshot
        {
            public bool IsValid;
            public string ErrorMessage;
            public string SourceName;
            public string TargetName;
            public double DistanceMeters;
            public double RequiredJumpMeters;
            public MotionStatus Status;
            public double RadialRateKmPerMinute;
            public ClosestResult Closest;
            public JumpWindow JumpWindow;
            public JumpInfo JumpInfo;
            public NavigationGeometry Geometry;
            public double ModelSeconds;
            public DateTime SampleTime;

            public static Snapshot Error(string message)
            {
                return new Snapshot { IsValid = false, ErrorMessage = message ?? "Unknown error" };
            }
        }

        private sealed class PanelAlertMemory
        {
            public bool WasIdeal;
            public AlertLevel LastLevel;
            public DateTime LastSoundAt = DateTime.MinValue;
        }

        private sealed class RouteSelection
        {
            public string SourceBody;
            public string TargetBody;
        }

        private sealed class AlertResult
        {
            public AlertLevel Level;
            public Color FontColor;
            public string StatusText;
            public int SoundBlocksFound;
            public bool SoundTriggered;
        }
    }
}

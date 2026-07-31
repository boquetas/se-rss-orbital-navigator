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
        private double RefineMinimum(BodyDef source, BodyDef target, double modelSeconds, double left, double right)
        {
            const double ratio = 0.6180339887498949;
            double x1 = right - (right - left) * ratio;
            double x2 = left + (right - left) * ratio;
            double f1 = DistanceAt(source, target, modelSeconds + x1);
            double f2 = DistanceAt(source, target, modelSeconds + x2);

            for (int index = 0; index < 32; index++)
            {
                if (f1 < f2)
                {
                    right = x2;
                    x2 = x1;
                    f2 = f1;
                    x1 = right - (right - left) * ratio;
                    f1 = DistanceAt(source, target, modelSeconds + x1);
                }
                else
                {
                    left = x1;
                    x1 = x2;
                    f1 = f2;
                    x2 = left + (right - left) * ratio;
                    f2 = DistanceAt(source, target, modelSeconds + x2);
                }
            }
            return (left + right) * 0.5;
        }

        private JumpWindow FindJumpWindow(BodyDef source, BodyDef target, double modelSeconds,
            double horizonSeconds, double rangeMeters, NavigationGeometry geometry)
        {
            JumpWindow result = new JumpWindow();
            if (rangeMeters <= 0 || horizonSeconds <= 1)
                return result;

            double step = Math.Max(10.0, Math.Min(60.0, horizonSeconds / 5000.0));
            double previousOffset = 0;
            double previousDistance = RequiredJumpDistance(DistanceAt(source, target, modelSeconds), geometry);
            bool inside = previousDistance <= rangeMeters;
            result.IsOpenNow = inside;
            if (inside)
            {
                result.Found = true;
                result.OpenSecondsFromNow = 0;
            }

            for (double offset = step; offset <= horizonSeconds; offset += step)
            {
                double currentDistance = RequiredJumpDistance(
                    DistanceAt(source, target, modelSeconds + offset), geometry);
                bool currentInside = currentDistance <= rangeMeters;

                if (!inside && currentInside && !result.Found)
                {
                    result.Found = true;
                    result.OpenSecondsFromNow = RefineCrossing(source, target, modelSeconds,
                        previousOffset, offset, rangeMeters, geometry);
                }
                else if (inside && !currentInside && result.Found)
                {
                    result.CloseSecondsFromNow = RefineCrossing(source, target, modelSeconds,
                        previousOffset, offset, rangeMeters, geometry);
                    result.HasClose = true;
                    return result;
                }

                inside = currentInside;
                previousOffset = offset;
            }

            return result;
        }

        private double RefineCrossing(BodyDef source, BodyDef target, double modelSeconds,
            double left, double right, double rangeMeters, NavigationGeometry geometry)
        {
            double leftValue = RequiredJumpDistance(
                DistanceAt(source, target, modelSeconds + left), geometry) - rangeMeters;
            for (int index = 0; index < 40; index++)
            {
                double middle = (left + right) * 0.5;
                double middleValue = RequiredJumpDistance(
                    DistanceAt(source, target, modelSeconds + middle), geometry) - rangeMeters;
                if ((leftValue <= 0 && middleValue <= 0) || (leftValue > 0 && middleValue > 0))
                {
                    left = middle;
                    leftValue = middleValue;
                }
                else
                {
                    right = middle;
                }
            }
            return (left + right) * 0.5;
        }

        private AlertResult ApplyAlerts(ModTerminalBlock panelBlock, PanelConfig config, Snapshot snapshot)
        {
            AlertResult result = new AlertResult();
            result.Level = AlertLevel.Normal;
            result.FontColor = config.NormalColor;

            if (!snapshot.IsValid)
            {
                result.Level = AlertLevel.Error;
                result.FontColor = config.ErrorColor;
                result.StatusText = "ERROR";
                return result;
            }

            if (!snapshot.Geometry.IsShipPositionKnown)
            {
                result.Level = AlertLevel.Error;
                result.FontColor = config.ErrorColor;
                result.StatusText = "POSITION UNKNOWN";

                PanelAlertMemory unknownMemory;
                if (!_alertMemory.TryGetValue(panelBlock.EntityId, out unknownMemory))
                    unknownMemory = new PanelAlertMemory();
                unknownMemory.WasIdeal = false;
                unknownMemory.LastLevel = result.Level;
                _alertMemory[panelBlock.EntityId] = unknownMemory;
                return result;
            }

            bool windowOpen = snapshot.JumpInfo.RangeMeters > 0
                && snapshot.JumpWindow.Found
                && snapshot.JumpWindow.IsOpenNow;
            bool opensSoon = snapshot.JumpInfo.RangeMeters > 0
                && snapshot.JumpWindow.Found
                && !snapshot.JumpWindow.IsOpenNow
                && snapshot.JumpWindow.OpenSecondsFromNow <= config.AlertLeadMinutes * 60.0;

            if (windowOpen && snapshot.Status == MotionStatus.Receding)
            {
                result.Level = AlertLevel.OpenReceding;
                result.FontColor = config.ClosingColor;
                result.StatusText = "OPEN - WINDOW CLOSING";
            }
            else if (windowOpen)
            {
                result.Level = AlertLevel.Open;
                result.FontColor = config.OpenColor;
                result.StatusText = "OPEN - JUMP WINDOW";
            }
            else if (opensSoon)
            {
                result.Level = AlertLevel.Soon;
                result.FontColor = config.SoonColor;
                result.StatusText = "OPENS SOON";
            }
            else
            {
                result.StatusText = "MONITORING";
            }

            bool jumpReady = snapshot.JumpInfo.Mode == JumpRangeMode.Manual
                ? snapshot.JumpInfo.RangeMeters > 0
                : snapshot.JumpInfo.RangeMeters > 0
                    && snapshot.JumpInfo.ReadyDrives > 0
                    && (!config.SoundRequireApiValid || snapshot.JumpInfo.IsJumpValid);
            bool idealNow = windowOpen && jumpReady;

            PanelAlertMemory memory;
            bool firstObservation = !_alertMemory.TryGetValue(panelBlock.EntityId, out memory);
            if (firstObservation)
                memory = new PanelAlertMemory();

            bool enteredIdeal = idealNow && !memory.WasIdeal;
            bool cooldownPassed = memory.LastSoundAt == DateTime.MinValue
                || (snapshot.SampleTime - memory.LastSoundAt).TotalSeconds >= config.SoundCooldownSeconds;
            bool shouldPlay = config.SoundAlertEnabled
                && idealNow
                && cooldownPassed
                && (enteredIdeal || (firstObservation && config.SoundOnStartup));

            if (shouldPlay)
            {
                result.SoundBlocksFound = PlayAlertSounds(panelBlock, config.SoundBlockTag);
                result.SoundTriggered = result.SoundBlocksFound > 0;
                if (result.SoundTriggered)
                    memory.LastSoundAt = snapshot.SampleTime;
            }
            else if (config.SoundAlertEnabled)
            {
                result.SoundBlocksFound = CountAlertSounds(panelBlock, config.SoundBlockTag);
            }

            memory.WasIdeal = idealNow;
            memory.LastLevel = result.Level;
            _alertMemory[panelBlock.EntityId] = memory;
            return result;
        }
    }
}

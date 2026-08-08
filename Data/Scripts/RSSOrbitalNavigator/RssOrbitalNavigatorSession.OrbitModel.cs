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
        private Snapshot BuildSnapshot(PanelConfig config, JumpInfo jumpInfo, ModTerminalBlock panelBlock)
        {
            BodyDef source;
            BodyDef target;
            if (!_bodies.TryGetValue(config.SourceBody, out source))
                return Snapshot.Error("Unknown SourceBody: " + config.SourceBody);
            if (!_bodies.TryGetValue(config.TargetBody, out target))
                return Snapshot.Error("Unknown TargetBody: " + config.TargetBody);

            NavigationGeometry geometry = BuildNavigationGeometry(panelBlock, source, target, config);
            DateTime sampleTime = MyAPIGateway.Session.GameDateTime;
            double epochSeconds = (sampleTime - config.ModelEpoch).TotalSeconds;
            double modelSeconds = epochSeconds * GlobalTimescale + config.TimeOffsetSeconds;
            double centerDistance = geometry.UsesLogicalShipPosition
                ? geometry.ShipToTargetDistanceMeters
                : DistanceAt(source, target, modelSeconds);
            double requiredJumpDistance = RequiredJumpDistance(centerDistance, geometry);
            if (geometry.UsesLogicalShipPosition)
            {
                UpdateShipTrajectory(panelBlock.EntityId, config, requiredJumpDistance, sampleTime, geometry);
                if (geometry.HasShipTrajectory)
                {
                    double margin = jumpInfo.RangeMeters - requiredJumpDistance;
                    double rate = geometry.ShipRequiredRateMetersPerSecond;
                    if ((margin > 0.0 && rate > 0.0) || (margin < 0.0 && rate < 0.0))
                    {
                        geometry.HasMarginForecast = true;
                        geometry.SecondsToMarginChange = Math.Abs(margin / rate);
                    }
                }
            }

            MotionStatus status = MotionStatus.Stable;
            if (geometry.UsesLogicalShipPosition)
            {
                status = MotionStatus.Stable;
            }

            double radialMetersPerSecond = 0.0;
            double rateKmPerMinute = 0.0;
            ClosestResult closest = default(ClosestResult);
            if (!geometry.UsesLogicalShipPosition)
            {
                double derivativeWindow = 10.0;
                double before = DistanceAt(source, target, modelSeconds - derivativeWindow);
                double after = DistanceAt(source, target, modelSeconds + derivativeWindow);
                radialMetersPerSecond = (after - before) / (derivativeWindow * 2.0);
                rateKmPerMinute = radialMetersPerSecond * 0.06;

                if (Math.Abs(rateKmPerMinute) < 0.01)
                    status = MotionStatus.Stable;
                else if (rateKmPerMinute < 0)
                    status = MotionStatus.Closing;
                else
                    status = MotionStatus.Receding;

                closest = FindNextClosest(source, target, modelSeconds, config.PredictionHours * 3600.0);
                if (closest.Found)
                    closest.RequiredJumpMeters = RequiredJumpDistance(closest.DistanceMeters, geometry);
            }

            JumpWindow jumpWindow = default(JumpWindow);
            if (geometry.UsesLogicalShipPosition && jumpInfo.RangeMeters > 0)
            {
                jumpWindow.IsOpenNow = requiredJumpDistance <= jumpInfo.RangeMeters;
                jumpWindow.Found = jumpWindow.IsOpenNow;
                if (!jumpWindow.IsOpenNow && geometry.HasShipTrajectory
                    && geometry.ShipRequiredRateMetersPerSecond < 0.0)
                {
                    double secondsToOpen = (requiredJumpDistance - jumpInfo.RangeMeters)
                        / -geometry.ShipRequiredRateMetersPerSecond;
                    if (secondsToOpen <= config.ShipForecastMinutes * 60.0)
                    {
                        jumpWindow.OpenSecondsFromNow = Math.Max(0.0, secondsToOpen);
                        jumpWindow.Found = true;
                        if (secondsToOpen <= 10.0)
                            jumpWindow.IsOpenNow = true;
                    }
                    else
                        jumpWindow.Found = false;
                }
                else if (jumpWindow.IsOpenNow)
                {
                    if (geometry.HasShipTrajectory
                        && geometry.ShipRequiredRateMetersPerSecond > 0.0)
                    {
                        double secondsToClose = (jumpInfo.RangeMeters - requiredJumpDistance)
                            / geometry.ShipRequiredRateMetersPerSecond;
                        if (secondsToClose <= config.ShipForecastMinutes * 60.0)
                        {
                            jumpWindow.CloseSecondsFromNow = Math.Max(0.0, secondsToClose);
                            jumpWindow.HasClose = true;
                            jumpWindow.DurationSeconds = secondsToClose;
                        }
                    }

                    if (!jumpWindow.HasClose && geometry.HasShipTrajectory
                        && geometry.ShipRequiredRateMetersPerSecond <= 0.0)
                        jumpWindow.DurationSeconds = config.ShipForecastMinutes * 60.0;
                }
            }
            else if (geometry.CanForecastShipPosition && jumpInfo.RangeMeters > 0)
                jumpWindow = FindJumpWindow(source, target, modelSeconds,
                    config.PredictionHours * 3600.0, jumpInfo.RangeMeters, geometry);

            if (geometry.UsesLogicalShipPosition || !geometry.IsShipPositionKnown)
                closest = default(ClosestResult);

            return new Snapshot
            {
                IsValid = true,
                SourceName = source.Name,
                TargetName = target.Name,
                DistanceMeters = centerDistance,
                RequiredJumpMeters = requiredJumpDistance,
                Status = status,
                RadialRateKmPerMinute = rateKmPerMinute,
                Closest = closest,
                JumpWindow = jumpWindow,
                JumpInfo = jumpInfo,
                Geometry = geometry,
                ModelSeconds = modelSeconds,
                SampleTime = sampleTime
            };
        }

        private void UpdateShipTrajectory(long panelId, PanelConfig config, double requiredJumpDistance,
            DateTime sampleTime, NavigationGeometry geometry)
        {
            ShipTrajectoryMemory memory;
            if (!_shipTrajectoryMemory.TryGetValue(panelId, out memory))
            {
                memory = new ShipTrajectoryMemory();
                _shipTrajectoryMemory[panelId] = memory;
            }

            string routeKey = config.SourceBody + "|" + config.TargetBody;
            if (!string.Equals(memory.RouteKey, routeKey, StringComparison.OrdinalIgnoreCase))
            {
                memory.RouteKey = routeKey;
                memory.HasSample = false;
                memory.HasRate = false;
            }

            if (memory.HasSample)
            {
                double seconds = (sampleTime - memory.LastSampleTime).TotalSeconds;
                if (seconds >= 1.0 && seconds <= 600.0)
                {
                    memory.RequiredRateMetersPerSecond = (requiredJumpDistance
                        - memory.LastRequiredDistanceMeters) / seconds;
                    memory.HasRate = true;
                }
            }

            memory.LastSampleTime = sampleTime;
            memory.LastRequiredDistanceMeters = requiredJumpDistance;
            memory.HasSample = true;
            geometry.HasShipTrajectory = memory.HasRate;
            geometry.ShipRequiredRateMetersPerSecond = memory.RequiredRateMetersPerSecond;
            geometry.CanForecastShipPosition = memory.HasRate;
        }

        private NavigationGeometry BuildNavigationGeometry(ModTerminalBlock panelBlock, BodyDef source,
            BodyDef target, PanelConfig config)
        {
            NavigationGeometry geometry = new NavigationGeometry();
            geometry.ConfiguredNavigationMode = config.NavigationMode;
            geometry.EffectiveNavigationMode = config.NavigationMode == NavigationMode.DeepSpace
                ? NavigationMode.DeepSpace : NavigationMode.Planetary;
            geometry.IsShipPositionKnown = config.NavigationMode != NavigationMode.DeepSpace;
            geometry.CanForecastShipPosition = config.NavigationMode != NavigationMode.DeepSpace;
            geometry.SourceMode = config.SourceRadiusMode;
            geometry.TargetMode = config.TargetArrivalMode;
            geometry.TargetSafetyMarginMeters = config.TargetSafetyMarginKm * 1000.0;

            if (config.NavigationMode == NavigationMode.DeepSpace)
            {
                geometry.SourceAllowanceMeters = 0;
                geometry.SourceDescription = "DEEP SPACE";
            }
            else if (config.SourceRadiusMode == SourceRadiusMode.Center)
            {
                geometry.SourceAllowanceMeters = 0;
                geometry.SourceDescription = "CENTER";
            }
            else if (config.SourceRadiusMode == SourceRadiusMode.Manual)
            {
                geometry.SourceAllowanceMeters = Math.Max(0.0, config.SourceDepartureRadiusKm * 1000.0);
                geometry.SourceDescription = "MANUAL";
            }
            else if (config.SourceRadiusMode == SourceRadiusMode.OrbitZone)
            {
                geometry.SourceAllowanceMeters = Math.Max(0.0, source.OrbitZoneRadiusMeters);
                geometry.SourceDescription = "ORBIT ZONE";
            }
            else
            {
                double detected;
                double voxelRadius;
                if (TryGetSourceCenterDistance(panelBlock, source, out detected, out voxelRadius))
                {
                    geometry.SourceAllowanceMeters = Math.Max(0.0, Math.Min(detected, source.OrbitZoneRadiusMeters));
                    geometry.SourceAutoDetected = true;
                    geometry.SourceVoxelRadiusMeters = voxelRadius;
                    geometry.SourceDescription = "AUTO";
                }
                else
                {
                    geometry.SourceAllowanceMeters = 0;
                    if (config.NavigationMode == NavigationMode.Auto)
                    {
                        geometry.EffectiveNavigationMode = NavigationMode.DeepSpace;
                        geometry.IsShipPositionKnown = false;
                        geometry.CanForecastShipPosition = false;
                        geometry.SourceDescription = "DEEP SPACE (AUTO)";
                    }
                    else
                    {
                        geometry.SourceDescription = "AUTO FAILED";
                        geometry.Warning = "Could not locate the source planet voxel near this grid; source allowance is 0 km.";
                    }
                }
            }

            if (geometry.EffectiveNavigationMode == NavigationMode.DeepSpace)
            {
                double shipToTargetDistance;
                if (TryGetRssShipToBodyDistance(panelBlock, target, _rssPlanets, out shipToTargetDistance))
                {
                    geometry.IsShipPositionKnown = true;
                    geometry.UsesLogicalShipPosition = true;
                    geometry.CanForecastShipPosition = false;
                    geometry.ShipToTargetDistanceMeters = shipToTargetDistance;
                }
            }

            if (config.TargetArrivalMode == TargetArrivalMode.Center)
            {
                geometry.TargetAllowanceMeters = 0;
                geometry.TargetDescription = "CENTER";
            }
            else if (config.TargetArrivalMode == TargetArrivalMode.Manual)
            {
                geometry.TargetAllowanceMeters = Math.Max(0.0, config.TargetArrivalRadiusKm * 1000.0);
                geometry.TargetDescription = "MANUAL";
            }
            else if (config.TargetArrivalMode == TargetArrivalMode.Surface)
            {
                geometry.TargetAllowanceMeters = Math.Max(0.0,
                    target.BodyRadiusMeters + geometry.TargetSafetyMarginMeters);
                geometry.TargetDescription = "SURFACE + MARGIN";
            }
            else
            {
                geometry.TargetAllowanceMeters = Math.Max(0.0,
                    target.OrbitZoneRadiusMeters - geometry.TargetSafetyMarginMeters);
                geometry.TargetDescription = "ORBIT ZONE";
            }

            return geometry;
        }

        private bool TryGetSourceCenterDistance(ModTerminalBlock panelBlock, BodyDef source,
            out double centerDistanceMeters, out double voxelRadiusMeters)
        {
            centerDistanceMeters = 0;
            voxelRadiusMeters = 0;
            if (panelBlock == null)
                return false;

            Vector3D panelPosition = panelBlock.GetPosition();
            double bestDistance = double.MaxValue;
            double bestRadius = 0;
            double minimumExpectedRadius = source.BodyRadiusMeters > 0
                ? source.BodyRadiusMeters * 0.40
                : 9000.0;

            foreach (IMyEntity entity in _largeVoxels)
            {
                if (entity == null || entity.MarkedForClose || entity.Closed)
                    continue;

                double radius = entity.WorldVolume.Radius;
                if (radius < minimumExpectedRadius)
                    continue;

                double distance = Vector3D.Distance(panelPosition, entity.WorldVolume.Center);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestRadius = radius;
                }
            }

            if (double.IsInfinity(bestDistance) || bestDistance == double.MaxValue)
                return false;

            double maximumPlausibleDistance = source.OrbitZoneRadiusMeters > 0
                ? source.OrbitZoneRadiusMeters * 1.15
                : Math.Max(source.BodyRadiusMeters * 3.0, 1000000.0);
            if (bestDistance > maximumPlausibleDistance)
                return false;

            centerDistanceMeters = bestDistance;
            voxelRadiusMeters = bestRadius;
            return true;
        }

        private static double RequiredJumpDistance(double centerDistanceMeters, NavigationGeometry geometry)
        {
            return Math.Max(0.0, centerDistanceMeters
                - geometry.SourceAllowanceMeters
                - geometry.TargetAllowanceMeters);
        }

        private double DistanceAt(BodyDef source, BodyDef target, double modelSeconds)
        {
            Vector3D sourcePosition = GetBodyPosition(source, modelSeconds, 0);
            Vector3D targetPosition = GetBodyPosition(target, modelSeconds, 0);
            return Vector3D.Distance(sourcePosition, targetPosition);
        }

        private Vector3D GetBodyPosition(BodyDef body, double modelSeconds, int depth)
        {
            if (body == null || depth > 16)
                return Vector3D.Zero;

            Vector3D parentPosition = Vector3D.Zero;
            if (!string.IsNullOrWhiteSpace(body.ParentName))
            {
                BodyDef parent;
                if (_bodies.TryGetValue(body.ParentName, out parent))
                    parentPosition = GetBodyPosition(parent, modelSeconds, depth + 1);
            }

            if (body.PeriodSeconds == 0 || body.SemimajorAxisMeters == 0)
                return parentPosition;

            double meanAnomaly = body.PhaseOffsetRadians
                + (Math.PI * 2.0 * modelSeconds / body.PeriodSeconds);
            meanAnomaly = NormalizeRadians(meanAnomaly);

            double eccentricAnomaly = SolveEccentricAnomaly(meanAnomaly, body.Eccentricity);
            double x = body.SemimajorAxisMeters * (Math.Cos(eccentricAnomaly) - body.Eccentricity);
            double z = body.SemimajorAxisMeters * Math.Sqrt(Math.Max(0.0, 1.0 - body.Eccentricity * body.Eccentricity))
                * Math.Sin(eccentricAnomaly);

            Vector3D local = new Vector3D(x, 0, z);
            double yaw = body.YawDegrees * Math.PI / 180.0;
            double pitch = body.PitchDegrees * Math.PI / 180.0;
            double roll = body.RollDegrees * Math.PI / 180.0;
            MatrixD rotation = MatrixD.CreateFromYawPitchRoll(yaw, pitch, roll);
            Vector3D rotated = Vector3D.TransformNormal(local, rotation);
            return parentPosition + rotated;
        }

        private static double SolveEccentricAnomaly(double meanAnomaly, double eccentricity)
        {
            if (eccentricity <= 0.0000001)
                return meanAnomaly;

            double eccentricAnomaly = eccentricity < 0.8 ? meanAnomaly : Math.PI;
            for (int index = 0; index < 12; index++)
            {
                double function = eccentricAnomaly - eccentricity * Math.Sin(eccentricAnomaly) - meanAnomaly;
                double derivative = 1.0 - eccentricity * Math.Cos(eccentricAnomaly);
                if (Math.Abs(derivative) < 0.000000001)
                    break;
                eccentricAnomaly -= function / derivative;
            }
            return eccentricAnomaly;
        }

        private static double NormalizeRadians(double angle)
        {
            double full = Math.PI * 2.0;
            angle %= full;
            if (angle < 0)
                angle += full;
            return angle;
        }

        private ClosestResult FindNextClosest(BodyDef source, BodyDef target, double modelSeconds, double horizonSeconds)
        {
            ClosestResult result = new ClosestResult();
            if (horizonSeconds <= 1)
                return result;

            double step = Math.Max(10.0, horizonSeconds / 3000.0);
            double dBefore = DistanceAt(source, target, modelSeconds);
            double dCurrent = DistanceAt(source, target, modelSeconds + step);

            for (double offset = step * 2.0; offset <= horizonSeconds; offset += step)
            {
                double dAfter = DistanceAt(source, target, modelSeconds + offset);
                if (dCurrent <= dBefore && dCurrent <= dAfter)
                {
                    double left = Math.Max(0, offset - step * 2.0);
                    double right = offset;
                    double refined = RefineMinimum(source, target, modelSeconds, left, right);
                    result.Found = true;
                    result.SecondsFromNow = refined;
                    result.DistanceMeters = DistanceAt(source, target, modelSeconds + refined);
                    return result;
                }
                dBefore = dCurrent;
                dCurrent = dAfter;
            }

            return result;
        }
    }
}

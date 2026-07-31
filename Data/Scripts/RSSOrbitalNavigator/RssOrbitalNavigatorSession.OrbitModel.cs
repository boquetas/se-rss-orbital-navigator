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
            double centerDistance = DistanceAt(source, target, modelSeconds);
            double requiredJumpDistance = RequiredJumpDistance(centerDistance, geometry);

            double derivativeWindow = 10.0;
            double before = DistanceAt(source, target, modelSeconds - derivativeWindow);
            double after = DistanceAt(source, target, modelSeconds + derivativeWindow);
            double radialMetersPerSecond = (after - before) / (derivativeWindow * 2.0);

            MotionStatus status;
            double rateKmPerMinute = radialMetersPerSecond * 0.06;
            if (Math.Abs(rateKmPerMinute) < 0.01)
                status = MotionStatus.Stable;
            else if (rateKmPerMinute < 0)
                status = MotionStatus.Closing;
            else
                status = MotionStatus.Receding;

            ClosestResult closest = FindNextClosest(source, target, modelSeconds, config.PredictionHours * 3600.0);
            if (closest.Found)
                closest.RequiredJumpMeters = RequiredJumpDistance(closest.DistanceMeters, geometry);

            JumpWindow jumpWindow = default(JumpWindow);
            if (jumpInfo.RangeMeters > 0)
                jumpWindow = FindJumpWindow(source, target, modelSeconds,
                    config.PredictionHours * 3600.0, jumpInfo.RangeMeters, geometry);

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

        private NavigationGeometry BuildNavigationGeometry(ModTerminalBlock panelBlock, BodyDef source,
            BodyDef target, PanelConfig config)
        {
            NavigationGeometry geometry = new NavigationGeometry();
            geometry.SourceMode = config.SourceRadiusMode;
            geometry.TargetMode = config.TargetArrivalMode;
            geometry.TargetSafetyMarginMeters = config.TargetSafetyMarginKm * 1000.0;

            if (config.SourceRadiusMode == SourceRadiusMode.Center)
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
                    geometry.SourceDescription = "AUTO FAILED";
                    geometry.Warning = "Could not locate the source planet voxel near this grid; source allowance is 0 km.";
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

            double step = Math.Max(10.0, Math.Min(120.0, horizonSeconds / 3000.0));
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

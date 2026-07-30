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
        private void UpdatePanels()
        {
            _entities.Clear();
            _largeVoxels.Clear();
            _processedBlocks.Clear();
            _cycleCache.Clear();

            MyAPIGateway.Entities.GetEntities(_entities, entity =>
                entity is IMyCubeGrid && !entity.MarkedForClose && !entity.Closed);

            MyAPIGateway.Entities.GetEntities(_largeVoxels, entity =>
            {
                if (entity == null || entity.MarkedForClose || entity.Closed)
                    return false;
                VoxelBase voxel = entity as VoxelBase;
                return voxel != null && entity.WorldVolume.Radius >= 9000.0;
            });

            foreach (IMyEntity entity in _entities)
            {
                IMyCubeGrid grid = entity as IMyCubeGrid;
                if (grid == null)
                    continue;

                IMyGridTerminalSystem terminalSystem = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid);
                if (terminalSystem == null)
                    continue;

                _terminalBlocks.Clear();
                terminalSystem.GetBlocks(_terminalBlocks);

                foreach (ModTerminalBlock block in _terminalBlocks)
                {
                    if (block == null || block.MarkedForClose || block.Closed)
                        continue;
                    if (!_processedBlocks.Add(block.EntityId))
                        continue;
                    if (block.CustomName == null || block.CustomName.IndexOf(PanelTag, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    PanelConfig config = PanelConfig.Parse(block.CustomData);
                    TextSurface surface = GetSurface(block, config.SurfaceIndex);
                    if (surface == null)
                        continue;

                    JumpInfo jumpInfo = BuildJumpInfo(block, config);
                    string cacheKey = config.SourceBody + "|" + config.TargetBody + "|"
                        + config.TimeOffsetSeconds.ToString("0.###", CultureInfo.InvariantCulture) + "|"
                        + config.ModelEpoch.Ticks.ToString(CultureInfo.InvariantCulture) + "|"
                        + config.PredictionHours.ToString("0.###", CultureInfo.InvariantCulture) + "|"
                        + block.EntityId.ToString(CultureInfo.InvariantCulture) + "|"
                        + jumpInfo.Mode.ToString() + "|"
                        + jumpInfo.RangeMeters.ToString("0.###", CultureInfo.InvariantCulture) + "|"
                        + jumpInfo.TotalDrives.ToString(CultureInfo.InvariantCulture) + "|"
                        + jumpInfo.ReadyDrives.ToString(CultureInfo.InvariantCulture) + "|"
                        + config.SourceRadiusMode.ToString() + "|"
                        + config.TargetArrivalMode.ToString() + "|"
                        + config.TargetSafetyMarginKm.ToString("0.###", CultureInfo.InvariantCulture);

                    Snapshot snapshot;
                    if (!_cycleCache.TryGetValue(cacheKey, out snapshot))
                    {
                        snapshot = BuildSnapshot(config, jumpInfo, block);
                        _cycleCache[cacheKey] = snapshot;
                    }

                    AlertResult alert = ApplyAlerts(block, config, snapshot);
                    WritePanel(surface, config, snapshot, alert);
                }
            }
        }

        private JumpInfo BuildJumpInfo(ModTerminalBlock panelBlock, PanelConfig config)
        {
            JumpInfo info = new JumpInfo();
            info.Mode = config.RangeMode;

            if (config.RangeMode == JumpRangeMode.Off)
                return info;

            if (config.RangeMode == JumpRangeMode.Manual)
            {
                info.RangeMeters = Math.Max(0.0, config.JumpRangeKm * 1000.0);
                info.ApiRangeMeters = info.RangeMeters;
                info.IsJumpValid = info.RangeMeters > 0;
                return info;
            }

            IMyCubeGrid grid = panelBlock.CubeGrid;
            if (grid == null)
            {
                info.ErrorMessage = "LCD grid unavailable.";
                return info;
            }

            info.IsStaticGrid = grid.IsStatic;
            info.IdentityId = ResolveJumpIdentityId(panelBlock, config);

            double storedPower = 0.0;
            double maximumPower = 0.0;
            foreach (ModTerminalBlock candidate in _terminalBlocks)
            {
                ModJumpDrive drive = candidate as ModJumpDrive;
                if (drive == null || drive.MarkedForClose || drive.Closed)
                    continue;

                bool sameConstruct;
                try
                {
                    sameConstruct = candidate.IsSameConstructAs(panelBlock);
                }
                catch
                {
                    sameConstruct = candidate.CubeGrid == panelBlock.CubeGrid;
                }

                if (!sameConstruct)
                    continue;

                info.TotalDrives++;
                if (drive.IsFunctional)
                    info.FunctionalDrives++;
                if (drive.Enabled && drive.IsWorking)
                    info.WorkingDrives++;
                if (drive.Status == Sandbox.ModAPI.Ingame.MyJumpDriveStatus.Ready)
                    info.ReadyDrives++;

                double maxPower = Math.Max(0.0, drive.MaxStoredPower);
                double currentPower = Math.Max(0.0, drive.CurrentStoredPower);
                maximumPower += maxPower;
                storedPower += Math.Min(currentPower, maxPower);
            }

            if (maximumPower > 0.0)
            {
                info.HasChargeData = true;
                info.ChargeRatio = Math.Max(0.0, Math.Min(1.0, storedPower / maximumPower));
            }

            if (grid.JumpSystem == null)
            {
                info.ErrorMessage = "Grid jump system unavailable.";
                return info;
            }

            try
            {
                info.ApiRangeMeters = Math.Max(0.0, grid.JumpSystem.GetMaxJumpDistance(info.IdentityId));
                info.IsJumpValid = !grid.IsStatic && grid.JumpSystem.IsJumpValid(info.IdentityId);
                info.RangeMeters = grid.IsStatic ? 0.0 : info.ApiRangeMeters;
            }
            catch (Exception exception)
            {
                info.ErrorMessage = "Jump API error: " + exception.Message;
                info.RangeMeters = 0.0;
            }

            return info;
        }

        private static long ResolveJumpIdentityId(ModTerminalBlock panelBlock, PanelConfig config)
        {
            if (config.JumpIdentityId != 0)
                return config.JumpIdentityId;
            if (panelBlock.OwnerId != 0)
                return panelBlock.OwnerId;

            IMyCubeGrid grid = panelBlock.CubeGrid;
            if (grid != null)
            {
                if (grid.BigOwners != null && grid.BigOwners.Count > 0)
                    return grid.BigOwners[0];
                if (grid.SmallOwners != null && grid.SmallOwners.Count > 0)
                    return grid.SmallOwners[0];
            }

            return 0;
        }
    }
}

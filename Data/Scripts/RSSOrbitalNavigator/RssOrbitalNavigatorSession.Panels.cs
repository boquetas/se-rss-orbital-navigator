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
using ModButtonPanel = SpaceEngineers.Game.ModAPI.IMyButtonPanel;
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
            _rssPlanets.Clear();
            _processedBlocks.Clear();
            _cycleCache.Clear();
            _navigationPanels.Clear();

            MyAPIGateway.Entities.GetEntities(_entities, entity =>
                entity is IMyCubeGrid && !entity.MarkedForClose && !entity.Closed);

            MyAPIGateway.Entities.GetEntities(_largeVoxels, entity =>
            {
                if (entity == null || entity.MarkedForClose || entity.Closed)
                    return false;
                VoxelBase voxel = entity as VoxelBase;
                return voxel != null && entity.WorldVolume.Radius >= 9000.0;
            });

            MyAPIGateway.Entities.GetEntities(_rssPlanets, entity =>
                entity is Sandbox.Game.Entities.MyPlanet && !entity.MarkedForClose && !entity.Closed);

            RequestRssApi();

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

                    _navigationPanels[block.EntityId] = block;

                    PanelConfig config = PanelConfig.Parse(block.CustomData);
                    ApplyRouteSelection(block, config);
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
                        + config.NavigationMode.ToString() + "|"
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

            RegisterNavigationButtons();
        }

        private void RegisterNavigationButtons()
        {
            foreach (ModTerminalBlock panel in _navigationPanels.Values)
            {
                IMyGridTerminalSystem terminalSystem = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(panel.CubeGrid);
                if (terminalSystem == null)
                    continue;

                _terminalBlocks.Clear();
                terminalSystem.GetBlocks(_terminalBlocks);
                foreach (ModTerminalBlock block in _terminalBlocks)
                {
                    ModButtonPanel buttonPanel = block as ModButtonPanel;
                    if (buttonPanel == null || buttonPanel.CustomName == null
                        || !HasNavigationAction(buttonPanel.CustomName))
                        continue;
                    if (_navigationButtons.ContainsKey(buttonPanel.EntityId))
                        continue;

                    Action<int> handler = button => NavigationButtonPressed(buttonPanel, button);
                    buttonPanel.ButtonPressed += handler;
                    _navigationButtons[buttonPanel.EntityId] = buttonPanel;
                    _navigationButtonHandlers[buttonPanel.EntityId] = handler;
                }
            }
        }

        private void NavigationButtonPressed(ModButtonPanel buttonPanel, int button)
        {
            if (buttonPanel == null || buttonPanel.CustomName == null)
                return;

            string name = buttonPanel.CustomName;
            bool multi = name.IndexOf("[RSSNAV MULTI", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("[RSSNAV CONTROLS", StringComparison.OrdinalIgnoreCase) >= 0;
            bool source = name.IndexOf("[RSSNAV SRC", StringComparison.OrdinalIgnoreCase) >= 0;
            bool destination = name.IndexOf("[RSSNAV DST", StringComparison.OrdinalIgnoreCase) >= 0;
            bool next = name.IndexOf("NEXT", StringComparison.OrdinalIgnoreCase) >= 0;
            bool previous = name.IndexOf("PREV", StringComparison.OrdinalIgnoreCase) >= 0;
            bool reset = name.IndexOf("RESET", StringComparison.OrdinalIgnoreCase) >= 0;
            if (multi)
            {
                source = button == 0 || button == 1;
                destination = button == 2 || button == 3;
                previous = button == 0 || button == 2;
                next = button == 1 || button == 3;
            }
            else if ((!source && !destination && !reset) || (!next && !previous && !reset))
                return;

            foreach (ModTerminalBlock panel in _navigationPanels.Values)
            {
                if (panel == null || !SameConstruct(panel, buttonPanel))
                    continue;

                PanelConfig config = PanelConfig.Parse(panel.CustomData);
                RouteSelection selection;
                if (!_routeSelections.TryGetValue(panel.EntityId, out selection))
                {
                    selection = new RouteSelection { SourceBody = config.SourceBody, TargetBody = config.TargetBody };
                    _routeSelections[panel.EntityId] = selection;
                }

                if (reset)
                {
                    selection.SourceBody = config.SourceBody;
                    selection.TargetBody = config.TargetBody;
                }
                else if (source)
                    selection.SourceBody = CycleBody(selection.SourceBody, next ? 1 : -1);
                else if (destination)
                    selection.TargetBody = CycleBody(selection.TargetBody, next ? 1 : -1);
            }
        }

        private void ApplyRouteSelection(ModTerminalBlock panel, PanelConfig config)
        {
            RouteSelection selection;
            if (!_routeSelections.TryGetValue(panel.EntityId, out selection))
            {
                selection = new RouteSelection { SourceBody = config.SourceBody, TargetBody = config.TargetBody };
                _routeSelections[panel.EntityId] = selection;
            }

            config.SourceBody = selection.SourceBody;
            config.TargetBody = selection.TargetBody;
        }

        private string CycleBody(string current, int direction)
        {
            if (_bodyNames.Count == 0)
                return current;
            int index = _bodyNames.IndexOf(current);
            if (index < 0)
                index = 0;
            index = (index + direction) % _bodyNames.Count;
            if (index < 0)
                index += _bodyNames.Count;
            return _bodyNames[index];
        }

        private static bool HasNavigationAction(string name)
        {
            return name.IndexOf("SRC NEXT", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("SRC PREV", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("DST NEXT", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("DST PREV", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("RESET", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("[RSSNAV MULTI", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("[RSSNAV CONTROLS", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool SameConstruct(ModTerminalBlock first, ModTerminalBlock second)
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

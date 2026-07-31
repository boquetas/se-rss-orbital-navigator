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
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public sealed partial class RssOrbitalNavigatorSession : MySessionComponentBase
    {
        private const string LogPrefix = "[RSS Orbital Navigator] ";
        private const string PanelTag = "[RSSNAV]";
        private const int UpdateEveryFrames = 300;
        private const int EmptyWorldRetryFrames = 3000;
        private const double GlobalTimescale = 1.0;

        private readonly HashSet<IMyEntity> _entities = new HashSet<IMyEntity>();
        private readonly HashSet<IMyEntity> _largeVoxels = new HashSet<IMyEntity>();
        private readonly HashSet<IMyEntity> _rssPlanets = new HashSet<IMyEntity>();
        private readonly List<ModTerminalBlock> _terminalBlocks = new List<ModTerminalBlock>();
        private readonly List<IMyPlayer> _players = new List<IMyPlayer>();
        private readonly HashSet<long> _processedBlocks = new HashSet<long>();
        private readonly Dictionary<string, BodyDef> _bodies = new Dictionary<string, BodyDef>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Snapshot> _cycleCache = new Dictionary<string, Snapshot>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<long, PanelAlertMemory> _alertMemory = new Dictionary<long, PanelAlertMemory>();
        private readonly Dictionary<long, ShipTrajectoryMemory> _shipTrajectoryMemory = new Dictionary<long, ShipTrajectoryMemory>();
        private readonly Dictionary<long, RouteSelection> _routeSelections = new Dictionary<long, RouteSelection>();
        private readonly Dictionary<long, ModTerminalBlock> _navigationPanels = new Dictionary<long, ModTerminalBlock>();
        private readonly Dictionary<long, ModButtonPanel> _navigationButtons = new Dictionary<long, ModButtonPanel>();
        private readonly Dictionary<long, Action<int>> _navigationButtonHandlers = new Dictionary<long, Action<int>>();
        private readonly List<string> _bodyNames = new List<string>();

        private int _frameCounter;
        private int _emptyWorldFrameCounter;
        private bool _hasCompletedPanelDiscovery;
        private bool _started;

        public override void BeforeStart()
        {
            BuildCatalog();
            LoadRssApi();
            _started = true;
            Log("Loaded v0.7.0. Zone-edge jump windows, visual alerts, and sound alerts enabled.");
        }

        public override void UpdateAfterSimulation()
        {
            if (!_started || MyAPIGateway.Session == null || !MyAPIGateway.Session.IsServer)
                return;

            _frameCounter++;
            if (_frameCounter < UpdateEveryFrames)
                return;

            _frameCounter = 0;
            if (_hasCompletedPanelDiscovery && _navigationPanels.Count == 0)
            {
                _emptyWorldFrameCounter += UpdateEveryFrames;
                if (_emptyWorldFrameCounter < EmptyWorldRetryFrames)
                    return;
                _emptyWorldFrameCounter = 0;
            }
            else
            {
                _emptyWorldFrameCounter = 0;
            }

            try
            {
                bool firstDiscovery = !_hasCompletedPanelDiscovery;
                UpdatePanels();
                _hasCompletedPanelDiscovery = true;
                if (firstDiscovery)
                    Log("Panel discovery found " + _navigationPanels.Count + " tagged panel(s).");
            }
            catch (Exception exception)
            {
                Log("Unhandled update error: " + exception);
            }
        }

        protected override void UnloadData()
        {
            _started = false;
            _entities.Clear();
            _largeVoxels.Clear();
            _rssPlanets.Clear();
            _terminalBlocks.Clear();
            _players.Clear();
            _processedBlocks.Clear();
            _bodies.Clear();
            _cycleCache.Clear();
            _alertMemory.Clear();
            _shipTrajectoryMemory.Clear();
            foreach (KeyValuePair<long, ModButtonPanel> entry in _navigationButtons)
            {
                try
                {
                    entry.Value.ButtonPressed -= _navigationButtonHandlers[entry.Key];
                }
                catch
                {
                }
            }
            _navigationButtons.Clear();
            _navigationButtonHandlers.Clear();
            _navigationPanels.Clear();
            _routeSelections.Clear();
            _bodyNames.Clear();
            _emptyWorldFrameCounter = 0;
            _hasCompletedPanelDiscovery = false;
            UnloadRssApi();
        }

        private void BuildCatalog()
        {
            _bodies.Clear();
            _bodyNames.Clear();
            _bodies["Trithorne"] = new BodyDef("Trithorne", null, 0, 0, 0, 0, 0, 0, 0, 20000000, 0);
            _bodyNames.Add("Trithorne");

            AddBody("Rennix", "Trithorne", 47170948, 0.33898634, -18.832943, 0, 0, 4750505, 3.2466464, 1250000, 0);
            AddBody("Tropol", "Rennix", 2996704.2, 0, 0, 0, 0, 38856.766, 5.248836, 499999.84, 100000.17);
            AddBody("Luburn", "Rennix", 5537485.5, 0, 0, 0, 0, 54374.203, 3.9908233, 697665.4, 139533.08);
            AddBody("Tebu", "Trithorne", 196301950, 0.33055303, -41.991955, 0, 0, 16440117, 3.987112, 4999999.5, 0);
            AddBody("Forsetti", "Tebu", 22987220, 0, 0, 0, 0, 116183.414, 4.3026037, 1999999.8, 0);
            AddBody("Thalion", "Forsetti", 1292046.5, 0, 0, 0, 0, 34113.582, 6.221331, 205168.81, 41033.775);
            AddBody("Lantha", "Tebu", 14545808, 0, 0, 0, 0, 120000, 0.037651002, 450000, 90000.17);
            AddBody("Cuprum", "Lantha", 838408.94, 0, 0, 0, 0, 37365.605, 6.2355614, 95000, 19000);
            AddBody("Taryx", "Tebu", 4215460, 0, 0, 0, 0, 16986.754, 4.9567704, 204584.7, 40916.943);
            AddBody("Wyrdel", "Trithorne", 120001390, 0, 0, 0, 0, 5242577, 4.3462286, 2999999.8, 0);
            // RSS models Nyph-Ea as Wyrdel's sibling. This fallback is approximate.
            AddBody("Nyph-Ea", "Trithorne", 1069032.1, 0, 0, 0, 0, 8559.362, 1.2116529, 999999.9, 0);
            AddBody("Frosti", "Wyrdel", 7847696, 0, 3.4145799, 0, 0, 41236.91, 5.0158973, 1750000, 0);
            AddBody("Onglax", "Frosti", 2037914, 0.103763394, 0, 0, 0, 19879.77, 3.4715874, 225000, 45000.084);
            AddBody("Ohova", "Wyrdel", 5814424, 0, -3.1209722, -0.05458545, 0, 161338.5, 1.8003793, 650000, 130000);
            AddBody("Qaale", "Ohova", 489073.78, 0, 0, 0, 0, 9374.072, 0.4689091, 220000, 44000.084);
        }

        private void AddBody(string name, string parent, double semimajorAxis, double eccentricity,
            double pitchDegrees, double rollDegrees, double yawDegrees, double periodSeconds,
            double phaseOffsetRadians, double orbitZoneRadiusMeters, double bodyRadiusMeters)
        {
            _bodies[name] = new BodyDef(name, parent, semimajorAxis, eccentricity,
                pitchDegrees, rollDegrees, yawDegrees, periodSeconds, phaseOffsetRadians,
                orbitZoneRadiusMeters, bodyRadiusMeters);
            _bodyNames.Add(name);
        }
    }
}

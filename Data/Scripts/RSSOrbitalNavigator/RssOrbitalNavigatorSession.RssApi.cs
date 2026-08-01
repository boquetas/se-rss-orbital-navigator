using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage;
using VRage.ModAPI;
using VRageMath;

using ModTerminalBlock = Sandbox.ModAPI.IMyTerminalBlock;

namespace Boquetas.RssOrbitalNavigator
{
    public sealed partial class RssOrbitalNavigatorSession
    {
        private const long RssApiChannel = 453273308835;

        private bool _rssApiRegistered;
        private bool _rssApiReady;
        private Func<Vector3D, Vector3D> _rssConvertRealPosToProxy;
        private Func<IMyEntity, MyTuple<bool, Vector3D, MatrixD>> _rssGetEntityZoneProxyPosition;
        private Func<MyPlanet, Vector3D> _rssGetBodyProxyPosition;

        private void LoadRssApi()
        {
            if (!_rssApiRegistered)
            {
                _rssApiRegistered = true;
                MyAPIGateway.Utilities.RegisterMessageHandler(RssApiChannel, HandleRssApiMessage);
            }

            RequestRssApi();
        }

        private void RequestRssApi()
        {
            if (!_rssApiReady)
                MyAPIGateway.Utilities.SendModMessage(RssApiChannel, "ApiEndpointRequest");
        }

        private void HandleRssApiMessage(object message)
        {
            if (string.Equals(message as string, "Compromised", StringComparison.OrdinalIgnoreCase))
            {
                _rssApiReady = false;
                Log("RSS logical-position API reported compromised; disabling integration.");
                return;
            }

            IReadOnlyDictionary<string, Delegate> methods = message as IReadOnlyDictionary<string, Delegate>;
            if (methods == null)
                return;

            try
            {
                _rssConvertRealPosToProxy = (Func<Vector3D, Vector3D>)methods["ConvertRealPosToProxy"];
                _rssGetEntityZoneProxyPosition =
                    (Func<IMyEntity, MyTuple<bool, Vector3D, MatrixD>>)methods["GetEntityZone_ProxyPosRot"];
                _rssGetBodyProxyPosition = (Func<MyPlanet, Vector3D>)methods["GetBodyProxyPosition"];
                _rssApiReady = true;
                Log("RSS logical-position API connected.");
            }
            catch (Exception exception)
            {
                _rssApiReady = false;
                _rssConvertRealPosToProxy = null;
                _rssGetEntityZoneProxyPosition = null;
                _rssGetBodyProxyPosition = null;
                Log("RSS logical-position API unavailable: " + exception.Message);
            }
        }

        private void UnloadRssApi()
        {
            if (_rssApiRegistered)
            {
                _rssApiRegistered = false;
                MyAPIGateway.Utilities.UnregisterMessageHandler(RssApiChannel, HandleRssApiMessage);
            }

            _rssApiReady = false;
            _rssConvertRealPosToProxy = null;
            _rssGetEntityZoneProxyPosition = null;
            _rssGetBodyProxyPosition = null;
        }

        private bool TryGetRssShipToBodyDistance(ModTerminalBlock panelBlock, BodyDef target,
            HashSet<IMyEntity> planets, out double distanceMeters)
        {
            distanceMeters = 0.0;
            if (!_rssApiReady || panelBlock == null || target == null
                || _rssConvertRealPosToProxy == null || _rssGetEntityZoneProxyPosition == null
                || _rssGetBodyProxyPosition == null)
                return false;

            MyPlanet targetPlanet = null;
            foreach (IMyEntity entity in planets)
            {
                MyPlanet planet = entity as MyPlanet;
                if (planet == null || planet.MarkedForClose || planet.Closed)
                    continue;
                if (string.Equals(planet.Name, target.Name, StringComparison.OrdinalIgnoreCase))
                {
                    targetPlanet = planet;
                    break;
                }
            }

            if (targetPlanet == null)
                return false;

            try
            {
                MyTuple<bool, Vector3D, MatrixD> shipPosition = _rssGetEntityZoneProxyPosition(panelBlock);
                Vector3D shipProxyPosition = shipPosition.Item2;
                if (!shipPosition.Item1)
                    shipProxyPosition = _rssConvertRealPosToProxy(panelBlock.GetPosition());
                Vector3D targetProxyPosition = _rssGetBodyProxyPosition(targetPlanet);
                distanceMeters = Vector3D.Distance(shipProxyPosition, targetProxyPosition);
                return !double.IsNaN(distanceMeters) && !double.IsInfinity(distanceMeters);
            }
            catch (Exception exception)
            {
                Log("RSS logical-position query failed: " + exception.Message);
                return false;
            }
        }
    }
}

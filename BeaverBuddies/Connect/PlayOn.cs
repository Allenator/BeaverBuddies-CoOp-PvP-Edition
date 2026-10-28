using BeaverBuddies.IO;
using BeaverBuddies.Players;
using Timberborn.Localization;
using Timberborn.SingletonSystem;
using Timberborn.TimeSystem;
using UnityEngine;

namespace BeaverBuddies.Connect
{
    /**
     * On the host, after everyone reloaded for a desync or a player joining: the game plays on at the speed it was at
     * once every player is back, or after a minute, with "Play on after a resync or a join" (Settings.PlayOnAfterReload).
     * A reload leaves the game paused for the host to press play, and a game left running stayed paused until someone
     * came back, six hours on October 25, 2026. A speed the host sets meanwhile is theirs, and the wait ends with it.
     */
    public class PlayOn : IPostLoadableSingleton, IUpdatableSingleton
    {
        private const float WaitSeconds = 60;

        private readonly SpeedManager _speedManager;
        private readonly ILoc _loc;
        private bool _waiting;
        private float _speed;
        private float _until;

        public PlayOn(SpeedManager speedManager, ILoc loc)
        {
            _speedManager = speedManager;
            _loc = loc;
        }

        public void PostLoad()
        {
            // Taken whatever this load is, so it never carries over to another
            ResyncService.ReloadReason? reason = ResyncService.TakeReloadReason();
            if (reason == null || EventIO.Get() is not ServerEventIO || !Settings.PlayOnAfterReload) return;
            _speed = ResyncService.SpeedBeforeReload;
            if (_speed <= 0) return;
            _waiting = true;
            _until = Time.unscaledTime + WaitSeconds;
            Plugin.Log($"Playing on at speed {_speed} once everyone is back ({reason})");
            SingletonManager.GetSingleton<DevTools.DevToolsService>()?.Notify(_loc.T("BeaverBuddies.Host.PlayOnSoon", _speed));
        }

        public void UpdateSingleton()
        {
            if (!_waiting) return;
            if (EventIO.Get() is not ServerEventIO server || _speedManager.CurrentSpeed > 0)
            {
                _waiting = false;
                Plugin.Log("Not playing on: the speed was set meanwhile, or the game isn't hosted any more");
                return;
            }
            int connected = server.NetBase?.ClientCount ?? 0;
            int back = PlayerPresence.Others().Count;
            if (back < connected && Time.unscaledTime < _until) return;
            _waiting = false;
            Plugin.Log($"Playing on at speed {_speed}: {back} of {connected} players back");
            // As the host's speed buttons do: everyone's game plays at it
            _speedManager.ChangeSpeed(_speed);
        }
    }
}

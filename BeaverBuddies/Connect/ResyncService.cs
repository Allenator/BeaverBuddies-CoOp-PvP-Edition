using BeaverBuddies.Events;
using BeaverBuddies.IO;
using System;
using Timberborn.CoreUI;
using Timberborn.GameSaveRepositorySystem;
using Timberborn.GameSceneLoading;
using Timberborn.SingletonSystem;

namespace BeaverBuddies.Connect
{
    /**
     * Reloads everyone's game from a save the host makes at the end of a
     * tick, sent over the connections they already have, with every machine
     * loading it with the same random seed, as a game starts. It gets every
     * player back in sync after a desync, and brings in a player joining a
     * game in progress. A running game can't be patched back into sync, or
     * sent as it is to someone joining, since loading a save rolls some
     * values afresh (tree lifespans and the like); all loading the same
     * save together works.
     */
    public class ResyncService : RegisteredSingleton, IUpdatableSingleton, IPostLoadableSingleton
    {
        private readonly RehostingService _rehostingService;
        private readonly GameSaveRepository _gameSaveRepository;
        private readonly GameSceneLoader _gameSceneLoader;
        private readonly DialogBoxShower _dialogBoxShower;

        // The desync the game was last reloaded for, to say so once it's back
        private static ClientDesyncedEvent _resyncedFrom;
        // A client's copy of the host's save, to load at the next update
        // rather than in the middle of a tick
        private static byte[] _receivedSave;
        // The game loading is a reload's
        private static bool _reloading;

        /** The game loading is a reload's: every machine loads the same save, the host's (see DesyncDetecterService.Reset). */
        public static bool Reloading => _reloading;

        // The host's resync, for ReplayService to start at the next update
        private bool _hostPending;
        private bool _loading;

        /** On the host, a desync is waiting for everyone to be reloaded. */
        public bool HostPending => _hostPending;

        /** Whether this game is being replaced by a reload's save: nothing more should play in it. */
        public bool IsReloading => _loading || _receivedSave != null;

        public ResyncService(
            RehostingService rehostingService,
            GameSaveRepository gameSaveRepository,
            GameSceneLoader gameSceneLoader,
            DialogBoxShower dialogBoxShower)
        {
            _rehostingService = rehostingService;
            _gameSaveRepository = gameSaveRepository;
            _gameSceneLoader = gameSceneLoader;
            _dialogBoxShower = dialogBoxShower;
        }

        /**
         * On the host, when a player's desync reaches it: everyone reloads
         * the game from a save made at the end of the tick.
         */
        public void StartAsHost(ClientDesyncedEvent desync)
        {
            if (_hostPending || IsReloading) return;
            Plugin.Log($"Resyncing everyone after the desync of {desync.playerID}");
            DesyncDetecter.TickLog.Dump("at the resync");
            _resyncedFrom = desync;
            _hostPending = true;
        }

        /**
         * On a client, when a player's desync reaches it: the host's save
         * is on its way.
         */
        public void WaitForHost(ClientDesyncedEvent desync)
        {
            _resyncedFrom ??= desync;
            DesyncDetecter.TickLog.Dump("at the resync");
        }

        /** The host's save has arrived over the connection (on the Update() thread). */
        public static void ReceiveSave(byte[] saveBytes)
        {
            Plugin.Log($"Received the host's reload save ({saveBytes.Length} bytes)");
            _receivedSave = saveBytes;
        }

        /** On the host: from now on nothing plays, until the reload's save has loaded. */
        public void BeginHostReload()
        {
            LastReloadReason = _hostPending ? ReloadReason.Resync : ReloadReason.Join;
            // A desync's report pauses the game as it plays, before this: the speed from before it
            SpeedBeforeReload = _hostPending && _speedBeforeResync is float speed ? speed
                : SingletonManager.GetSingleton<ReplayService>()?.TargetSpeed ?? 0;
            _speedBeforeResync = null;
            Plugin.Log($"Why everyone reloads: {LastReloadReason}, from speed {SpeedBeforeReload}");
            _hostPending = false;
            _loading = true;
        }

        /** Why the host reloads everyone: a player joining a game in progress, or a desync. */
        public enum ReloadReason { Join, Resync }

        // On the host, why everyone last reloaded and the speed the game was at then, for what the game does once it's
        // back. Kept past the reload, which replaces this service with the scene
        public static ReloadReason? LastReloadReason { get; private set; }
        public static float SpeedBeforeReload { get; private set; }

        // The speed the game was at as the first report of a desync paused it
        private static float? _speedBeforeResync;

        /** A desync's report is about to pause the game: the speed it's at, for after the resync (the first report's). */
        public static void NoteSpeedBeforeResync(float speed) => _speedBeforeResync ??= speed;

        /** The reason of the reload just done, once: a load after it that isn't one has none (see PlayOn). */
        public static ReloadReason? TakeReloadReason()
        {
            ReloadReason? reason = LastReloadReason;
            LastReloadReason = null;
            return reason;
        }

        /**
         * On the host, at the end of a tick: saves the game, sends the save
         * to every player (and those waiting to join), and loads it here.
         */
        public void SaveAndReload(ServerEventIO server, bool joinOnly, Action onJoinGone)
        {
            bool saving = _rehostingService.SaveRehostFile(saveReference =>
            {
                // Stopped hosting in the meantime
                if (EventIO.Get() != server) return;
                // For tests (BB_SLOW_JOIN_SAVE, in ms): a join's save that takes a while, for the player joining to
                // leave as it's made
                if (joinOnly && DevTools.TestHarness.Active && int.TryParse(Environment.GetEnvironmentVariable("BB_SLOW_JOIN_SAVE"), out int slow))
                {
                    Plugin.Log($"[Test] The join's save takes {slow} ms longer");
                    System.Threading.Thread.Sleep(slow);
                }
                // The one who asked to join dropped while it saved, and nobody else needs the reload
                if (joinOnly && server.CancelJoinIfGone())
                {
                    Plugin.Log("The player joining left before the game was saved: not reloading");
                    _loading = false;
                    // No reload to have a reason: a later load that isn't one mustn't take this one's (see PlayOn)
                    TakeReloadReason();
                    onJoinGone();
                    return;
                }
                try
                {
                    byte[] saveBytes = ServerHostingUtils.GetMapBtyes(_gameSaveRepository, saveReference);
                    Plugin.Log($"Reloading everyone ({server.NetBase.ClientCount} players) from a save of {saveBytes.Length} bytes");
                    server.Reload(saveBytes);
                    SingletonManager.Reset();
                    // The clients seed theirs from the same bytes
                    DeterminismService.InitGameStartState(saveBytes);
                    _reloading = true;
                    _gameSceneLoader.StartSaveGame(saveReference);
                }
                catch (Exception e)
                {
                    Plugin.LogError($"Failed to reload: {e}");
                    Fail(server);
                }
            }, true);
            if (!saving) Fail(server);
        }

        public void UpdateSingleton()
        {
            if (_loading || _receivedSave == null) return;
            byte[] saveBytes = _receivedSave;
            _receivedSave = null;
            _loading = true;
            _reloading = true;
            if (!ClientConnectionService.LoadReceivedMap(_gameSaveRepository, _gameSceneLoader, saveBytes))
            {
                Fail(null);
            }
        }

        private void Fail(ServerEventIO server)
        {
            // Whoever was waiting to join is turned away
            server?.TurnAwayJoins();
            bool resyncing = _resyncedFrom != null;
            _loading = false;
            _reloading = false;
            _resyncedFrom = null;
            string messageKey = resyncing ? "BeaverBuddies.ClientDesynced.ResyncFailed"
                : server != null ? "BeaverBuddies.JoinInProgress.FailedToReload"
                : null;
            if (messageKey == null) return;
            _dialogBoxShower.Create()
                .SetLocalizedMessage(messageKey)
                .Show();
        }

        public void PostLoad()
        {
            // What arrived after the save is for this game
            if (EventIO.Get() is ClientEventIO client) client.NetBase?.EndReload();

            ClientDesyncedEvent desync = _resyncedFrom;
            bool reloaded = _reloading;
            _resyncedFrom = null;
            _reloading = false;
            if (!reloaded || desync == null) return;
            Plugin.Log("Back in sync after a resync");
            ReplayService replayService = SingletonManager.GetSingleton<ReplayService>();
            if (replayService != null) desync.ShowResynced(replayService);
        }
    }
}

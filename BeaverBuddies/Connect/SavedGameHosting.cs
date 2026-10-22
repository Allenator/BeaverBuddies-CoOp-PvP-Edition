using BeaverBuddies.IO;
using Timberborn.CoreUI;
using Timberborn.GameSaveRepositorySystem;
using Timberborn.GameSaveRepositorySystemUI;
using Timberborn.SingletonSystem;

namespace BeaverBuddies.Connect
{
    /**
     * A saved game hosted the way a new game is, then loaded again when the
     * host starts it. Its server is up before the host loads it, offering the
     * save as it is, so players can join while it loads, and the host waits
     * in its box over the game it loaded, frozen, with what a new game's box
     * has: the players by name as they join, and the guests' lobby. Start
     * loads the save again, from the same seed as every guest, so they all
     * start from the same load: a game that's been loaded isn't quite the
     * one its save loads (see ServerEventIO).
     */
    public class SavedGameHosting : IPostLoadableSingleton, IUpdatableSingleton
    {
        // The save being hosted and its server, until the game it loaded first shows the box
        private static ServerEventIO _io;
        private static SaveReference _save;
        private static byte[] _data;

        private readonly DialogBoxShower _dialogBoxShower;
        private readonly ValidatingGameLoader _validatingGameLoader;
        // This game is the save loaded to host it, not the one it was hosted from, which goes on
        // for a few frames after and mustn't take the box with it
        private bool _hostsHere;

        public SavedGameHosting(DialogBoxShower dialogBoxShower, ValidatingGameLoader validatingGameLoader)
        {
            _dialogBoxShower = dialogBoxShower;
            _validatingGameLoader = validatingGameLoader;
        }

        /** Hosts the save, once it's found valid: its server first, then the game it loads. */
        public static void Begin(ValidatingGameLoader loader, DialogBoxShower shower, SaveReference saveReference)
        {
            NewGameHosting.Pending = false;
            var sceneLoader = loader._gameSceneLoader;
            byte[] data = ServerHostingUtils.GetMapBtyes(sceneLoader._gameSaveRepository, saveReference);
            Plugin.Log($"Reading map with length {data.Length}");
            // Hosted from a game: it ticks no more until the save replaces it, or a player joining
            // this server meanwhile would be sent its ticks, for a game they don't have
            SingletonManager.GetSingleton<ReplayService>()?.FreezeForHosting();
#if IS_STEAM
            Matchmaking.HostedListing.Hosting(saveReference, data);
#endif
            HostLobby.MapName = saveReference.SaveName;
            // Waiting before it listens: whoever joins is told the host is
            ServerEventIO io = new ServerEventIO { Stage = ServerEventIO.HostingStage.Waiting };
            EventIO.Set(io);
            if (!io.Start(data))
            {
                // Not left as the game's IO with nothing behind it
                EventIO.Reset();
                shower.Create().SetLocalizedMessage("BeaverBuddies.Menu.HostFailed").Show();
                return;
            }
#if IS_STEAM
            Matchmaking.HostedListing.Publish();
            Matchmaking.MatchmakingSession.ServerStarted();
#endif
            // What the game loaded now does is setting up, not played: it's loaded again (see ModBridge.AboutToHost)
            NewGameHosting.SettingUp = true;
            ServerHostingUtils.HoldPausedFromLoad();
            _io = io;
            _save = saveReference;
            _data = data;
            sceneLoader.StartSaveGame(saveReference);
        }

        public void PostLoad()
        {
            _hostsHere = _io != null && EventIO.Get() == _io;
        }

        public void UpdateSingleton()
        {
            // Once, in the game the save loaded first, still hosted and not started
            if (!_hostsHere) return;
            _hostsHere = false;
            if (_io == null || EventIO.Get() != _io || _io.Stage != ServerEventIO.HostingStage.Waiting) return;
            var io = _io;
            var save = _save;
            var data = _data;
            _io = null;
            _save = null;
            _data = null;
            HostingCover.Show();
            ServerHostingUtils.HostLoadedSave(_dialogBoxShower, _validatingGameLoader._gameSceneLoader, io, save, data);
        }
    }
}

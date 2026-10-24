using System;
using System.Linq;
using BeaverBuddies.IO;
using Timberborn.CoreUI;
using Timberborn.Localization;
using Timberborn.SingletonSystem;
using UnityEngine.UIElements;

namespace BeaverBuddies.Connect
{
    /**
     * What a guest sees of a game its host hasn't started yet: the host's
     * box without the code and the start, over the same background, until
     * the host presses Start. The guest has loaded the game by then, and it
     * stands paused at its first tick behind this.
     */
    public class GuestLobby : RegisteredSingleton, IUpdatableSingleton
    {
        private readonly DialogBoxShower _dialogBoxShower;
        private readonly ILoc _loc;
        private readonly PanelStack _panelStack;
        private DialogBox _box;
        private HostLobby _lobby;
        // Heard the host's presence say it's waiting, so what it says next means something
        private bool _heardWaiting;
        // The host started: the box goes once it's on top again (see TryClose)
        private bool _closing;

        public GuestLobby(DialogBoxShower dialogBoxShower, ILoc loc, PanelStack panelStack)
        {
            _dialogBoxShower = dialogBoxShower;
            _loc = loc;
            _panelStack = panelStack;
        }

        public static void Open(string mapName) => SingletonManager.GetSingleton<GuestLobby>()?.Show(mapName);

        /** Up on this guest's screen: the host hasn't started the game, and it's quiet there on purpose (see HostSilence). */
        public static bool IsOpen => SingletonManager.GetSingleton<GuestLobby>() is { _box: not null, _closing: false };

        private void Show(string mapName)
        {
            if (_box != null || EventIO.Get() is not ClientEventIO) return;
            Plugin.Log("The host hasn't started the game: waiting in its lobby");
            _lobby = new HostLobby(_dialogBoxShower, null, mapName);
            HostingCover.Show();
            // The host said it was waiting when this opened, so the next thing it says that isn't is the start
            _heardWaiting = true;
            _box = _dialogBoxShower.Create()
                .SetMessage("")
                .SetConfirmButton(() => { }, "")
                .SetCancelButton(Leave, _loc.T(Timberborn.Common.CommonLocKeys.CancelKey))
                .AddContent(_lobby.Build())
                .Show();
            // Nothing for a guest to confirm
            _box._root.Q<Button>("ConfirmButton")?.ToggleDisplayStyle(false);
        }

        public void UpdateSingleton()
        {
            if (_box == null) return;
            // Closed from outside (a dialog's OK pressed on it, say): gone, as it would go by itself
            if (!InStack(_box))
            {
                Forget();
                return;
            }
            if (_closing)
            {
                TryClose();
                return;
            }
            _lobby.Update();
            bool? waiting = Players.PlayerPresence.HostWaiting;
            if (waiting == true) _heardWaiting = true;
            // The host started, or is gone
            if (_heardWaiting && waiting == false || EventIO.IsNull) Close();
        }

        private void Close()
        {
            Plugin.Log("The host started the game: leaving its lobby");
            HostingCover.Hide();
            _closing = true;
            TryClose();
        }

        // The game only closes the dialog on top, and throws for any other, which stops the game: another
        // over the lobby (the warning a host on another build brings, say) has it wait until that one's answered
        private void TryClose()
        {
            if (!InStack(_box))
            {
                Forget();
                return;
            }
            if (_panelStack._stack.Peek().PanelController != _box) return;
            try { _box.Close(); }
            catch (ArgumentException) { return; }
            catch (InvalidOperationException) { }
            Forget();
        }

        private bool InStack(DialogBox box) => _panelStack._stack.Any(entry => entry.PanelController == box);

        private void Forget()
        {
            _box = null;
            _lobby = null;
            _closing = false;
            HostingCover.Hide();
        }

        // Out of the host's game, to the Multiplayer page
        private void Leave()
        {
            _box = null;
            _lobby = null;
            _closing = false;
            EventIO.Get()?.Close();
            EventIO.Reset();
            HostingCover.Leave();
        }
    }
}

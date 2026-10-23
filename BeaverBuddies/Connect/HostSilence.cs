using System;
using Timberborn.CoreUI;
using UnityEngine.UIElements;

namespace BeaverBuddies.Connect
{
    /**
     * On a client, says so when nothing has come from the host for a while.
     * A connection can go quiet without ever closing (a Steam or WebRTC
     * session that stalls), and the game would only stand still. The box
     * goes away by itself once the host is heard from again.
     */
    public class HostSilence
    {
        // The host sends something every couple of seconds, paused too (see
        // PlayerPresence), so this is well past a slow frame or a save
        public const double SilentSeconds = 15;

        private readonly DialogBoxShower _shower;
        private readonly Action _leave;
        private DialogBox _box;
        private Label _message;
        private int _shownSeconds = -1;
        // Waiting was picked: not again until the host has been heard from
        private bool _dismissed;

        public HostSilence(DialogBoxShower shower, Action leave)
        {
            _shower = shower;
            _leave = leave;
        }

        public void Update(double secondsSinceHeard)
        {
            if (secondsSinceHeard < SilentSeconds)
            {
                if (_box != null) Plugin.Log("Heard from the host again");
                _dismissed = false;
                Close();
                return;
            }
            if (_dismissed) return;
            // Closed some other way, like the scene going
            if (_box != null && _box._root?.panel == null) _box = null;
            if (_box == null) Show();
            int seconds = (int)secondsSinceHeard;
            if (seconds == _shownSeconds || _message == null) return;
            _shownSeconds = seconds;
            _message.text = _shower._loc.T("BeaverBuddies.Client.HostSilent", seconds);
        }

        public void Close()
        {
            if (_box == null) return;
            if (_box._root?.panel != null) _box.Close();
            _box = null;
            _message = null;
            _shownSeconds = -1;
        }

        private void Show()
        {
            Plugin.Log($"Nothing from the host for {SilentSeconds} s");
            _box = _shower.Create()
                .SetMessage("")
                .SetConfirmButton(() =>
                {
                    _box = null;
                    Plugin.Log("Leaving the game: the host went quiet");
                    _leave();
                }, _shower._loc.T("BeaverBuddies.Client.HostSilent.Leave"))
                .SetCancelButton(() =>
                {
                    _box = null;
                    _dismissed = true;
                }, _shower._loc.T("BeaverBuddies.Client.HostSilent.Wait"))
                .Show();
            _message = _box._root.Q<Label>("Message");
        }
    }
}

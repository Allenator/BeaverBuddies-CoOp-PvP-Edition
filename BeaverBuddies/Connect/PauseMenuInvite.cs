using BeaverBuddies.IO;
using Timberborn.CoreUI;
using Timberborn.Localization;
using UnityEngine.UIElements;

namespace BeaverBuddies.Connect
{
    /**
     * Invite Friends in the game menu (and for mods, see ModBridge.Invite),
     * for a host who closed the hosting box: the game's join code to pass
     * on, and Steam friends to invite (see Invites). Started or not, a
     * player gets into the game either way.
     */
    public class PauseMenuInvite : RegisteredSingleton
    {
        private const string ButtonName = "InviteFriendsButton";

        private readonly DialogBoxShower _dialogBoxShower;
        private readonly ILoc _loc;

        public PauseMenuInvite(DialogBoxShower dialogBoxShower, ILoc loc)
        {
            _dialogBoxShower = dialogBoxShower;
            _loc = loc;
        }

        public void AddButton(VisualElement panel)
        {
            Button button = ButtonInserter.DuplicateOrGetButton(panel, "LoadGameButton", ButtonName, button =>
            {
                button.text = _loc.T("BeaverBuddies.Host.InviteFriends");
                button.clicked += Invite;
            });
            button.ToggleDisplayStyle(Invites.CanInvite);
        }

        public void Invite()
        {
            if (Invites.CanInvite) Invites.Show(_dialogBoxShower);
        }
    }
}

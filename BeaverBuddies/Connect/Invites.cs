using BeaverBuddies.IO;
using System.Runtime.CompilerServices;
using Timberborn.CoreUI;
using Timberborn.Localization;
using UnityEngine;
using UnityEngine.UIElements;

namespace BeaverBuddies.Connect
{
    /**
     * Inviting players to the hosted game: its join code with a button to
     * copy it, for anyone to join by (Epic Games players, or friends on
     * Discord), and with Steam the online friends to invite straight to
     * the game's lobby (see Steam.SteamInvites). Free of Steam, so the Epic
     * Games build has it too.
     */
    public static class Invites
    {
        public static bool CanInvite => EventIO.Get() is ServerEventIO && WebRtc.JoinCode.Current != null;

        public static void Show(DialogBoxShower shower)
        {
            ILoc loc = shower._loc;
            string code = WebRtc.JoinCode.Display(WebRtc.JoinCode.Current);
            var content = new VisualElement();
            content.style.marginTop = 12;

            var codeRow = new VisualElement();
            codeRow.style.flexDirection = FlexDirection.Row;
            codeRow.style.alignItems = Align.Center;
            codeRow.style.justifyContent = Justify.Center;
            codeRow.style.marginBottom = 12;
            var label = new Label(loc.T("BeaverBuddies.Host.JoinCode", $"<b>{code}</b>"));
            label.AddToClassList("text--default");
            label.style.fontSize = 18;
            label.style.marginRight = 12;
            var copy = new LocalizableButton { text = loc.T("BeaverBuddies.Host.CopyCode") };
            copy.AddToClassList("menu-button");
            copy.style.width = 120;
            copy.RegisterCallback((ClickEvent _) =>
            {
                GUIUtility.systemCopyBuffer = code;
                copy.text = loc.T("BeaverBuddies.Host.CodeCopied");
            });
            codeRow.Add(label);
            codeRow.Add(copy);
            content.Add(codeRow);

            string message = loc.T("BeaverBuddies.Host.InviteByCode");
            if (Matchmaking.Lobbies.SteamUp && AddSteamFriends(content, loc, out string friendsMessage)) message += "\n\n" + friendsMessage;

            shower.Create()
                .SetMessage(message)
                .AddContent(content)
                .SetConfirmButton(() => { }, loc.T("BeaverBuddies.Menu.Back"))
                .Show();
        }

        // Apart, so a copy without Steam never compiles what touches it
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool AddSteamFriends(VisualElement content, ILoc loc, out string message) =>
            Steam.SteamInvites.AddFriends(content, loc, out message);
    }
}

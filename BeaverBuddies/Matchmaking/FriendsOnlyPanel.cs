using BeaverBuddies.Connect;
using Timberborn.CoreUI;
using Timberborn.Localization;
using Timberborn.MainMenuPanels;
using UnityEngine.UIElements;

namespace BeaverBuddies.Matchmaking
{
    /**
     * The "Steam friends only" box on the New Game screens' last page (a copy of the game's Tutorial
     * checkbox, under it) and beside Host on the Load Game screen: a hosted game is listed for anyone
     * unless the host ticks it.
     */
    public class FriendsOnlyPanel : RegisteredSingleton
    {
        private readonly VisualElementLoader _visualElementLoader;
        private readonly ILoc _loc;

        public FriendsOnlyPanel(VisualElementLoader visualElementLoader, ILoc loc)
        {
            _visualElementLoader = visualElementLoader;
            _loc = loc;
        }

        // Hosted games are listed for anyone unless the host ticks this; the box beside Tutorial
        public static bool FriendsOnly { get; private set; }
        public const string FriendsOnlyWrapper = "BeaverBuddiesFriendsOnlyWrapper";
        // The box on the page on screen, for the test harness
        internal static VisualElement FriendsOnlyBox;

        // A copy of the game's Tutorial checkbox, under it
        private void AddFriendsOnly(NewGameModePanel panel)
        {
            if (panel._root.Q(FriendsOnlyWrapper) != null) return;
            VisualElement tutorial = panel._root.Q("TutorialToggleWrapper");
            if (tutorial?.parent == null) return;
            VisualElement wrapper = FriendsOnlyToggle();
            if (wrapper == null) return;
            wrapper.ToggleDisplayStyle(false);
            tutorial.parent.Insert(tutorial.parent.IndexOf(tutorial) + 1, wrapper);
            FriendsOnlyBox = wrapper;
        }

        // The same box beside Host on the Load Game screen, ticked the same way
        public void AddFriendsOnly(Button host, bool visible)
        {
            VisualElement parent = host?.parent;
            if (parent == null) return;
            VisualElement wrapper = parent.Q(FriendsOnlyWrapper);
            if (wrapper == null)
            {
                wrapper = FriendsOnlyToggle();
                if (wrapper == null) return;
                wrapper.style.alignSelf = Align.Center;
                // Pushes it and Host to the right, away from the delete buttons
                wrapper.style.marginLeft = StyleKeyword.Auto;
                parent.Insert(parent.IndexOf(host), wrapper);
            }
            wrapper.Q<Toggle>("FriendsOnlyToggle").SetValueWithoutNotify(FriendsOnly);
            wrapper.ToggleDisplayStyle(visible);
            if (visible) FriendsOnlyBox = wrapper;
        }

        private VisualElement FriendsOnlyToggle()
        {
            VisualElement wrapper = _visualElementLoader.LoadVisualElement("MainMenu/NewGameModePanel").Q("TutorialToggleWrapper");
            if (wrapper == null) return null;
            for (VisualElement e = wrapper.parent; e != null; e = e.parent)
            {
                for (int i = 0; i < e.styleSheets.count; i++) wrapper.styleSheets.Add(e.styleSheets[i]);
            }
            wrapper.RemoveFromHierarchy();
            wrapper.name = FriendsOnlyWrapper;
            wrapper.Q<Label>(className: "new-game-mode-panel__tutorial-label").text = _loc.T("BeaverBuddies.Match.FriendsOnly");
            Toggle toggle = wrapper.Q<Toggle>("TutorialToggle");
            toggle.name = "FriendsOnlyToggle";
            toggle.tooltip = _loc.T("BeaverBuddies.Match.FriendsOnly.Tooltip");
            toggle.SetValueWithoutNotify(FriendsOnly);
            toggle.RegisterValueChangedCallback(e => FriendsOnly = e.newValue);
            return wrapper;
        }

        // Only when hosting: invites or the join code are the ways in, Steam or not
        public static void ShowFriendsOnly(NewGameModePanel panel, bool visible)
        {
            panel._root?.Q(FriendsOnlyWrapper)?.ToggleDisplayStyle(visible);
        }

        public void AddTo(NewGameModePanel panel) => AddFriendsOnly(panel);
    }
}

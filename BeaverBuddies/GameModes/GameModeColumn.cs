using System;
using System.Collections.Generic;
using System.Linq;
using Bindito.Core;
using HarmonyLib;
using Timberborn.CoreUI;
using Timberborn.Localization;
using Timberborn.MainMenuPanels;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameModes
{
    [Context("MainMenu")]
    public class GameModesConfigurator : IConfigurator
    {
        public void Configure(IContainerDefinition containerDefinition) => containerDefinition.Bind<GameModeColumn>().AsSingleton();
    }

    /**
     * The game modes on the New Game screens' last page: a column between
     * the difficulties and their details, built like the difficulties'
     * column (a scrolling list of the same buttons, so any number of modes
     * fit), with a line about the one picked under the difficulty's own, in
     * the details, and its name in the summary. Only when there's a choice:
     * with Classic alone the page is the game's own.
     *
     * Other mods' columns of the same kind are built with Column and Button.
     */
    public class GameModeColumn
    {
        public const string ContainerName = "GameModesColumn";
        // The difficulties' own look: their buttons, the selected one, and their scrolling list
        private const string ButtonTemplate = "MainMenu/NewGameModeButton";
        public const string SelectedClass = "new-game-mode-panel__mode--selected";
        private static readonly string[] ListClasses = { "new-game-mode-panel__modes", "scroll--green-decorated" };
        private const int DetailsWidth = 280;

        internal static GameModeColumn Instance { get; private set; }

        private readonly VisualElementLoader _visualElementLoader;
        private readonly ILoc _loc;
        private NewGameModePanel _panel;
        private ScrollView _list;
        private Label _about;
        private bool _alone;

        public GameModeColumn(VisualElementLoader visualElementLoader, ILoc loc)
        {
            _visualElementLoader = visualElementLoader;
            _loc = loc;
            Instance = this;
            Modes.Picked -= OnPicked;
            Modes.Picked += OnPicked;
        }

        private static bool HasChoice(bool alone) => Modes.For(alone).Count > 1;

        internal void AddTo(NewGameModePanel panel)
        {
            _panel = panel;
            if (panel._root.Q(ContainerName) != null || !HasChoice(true) && !HasChoice(false)) return;
            VisualElement difficulties = panel._root.Q("Modes");
            if (difficulties?.parent == null) return;
            VisualElement column = Column(panel, out _list, out _about);
            column.name = ContainerName;
            difficulties.parent.Insert(difficulties.parent.IndexOf(difficulties) + 1, column);
            Refresh();
        }

        /**
         * A column of choices beside the difficulties, after any added
         * before it: its scrolling list, and the line about its pick, under
         * the difficulty's. The difficulty's details wrap instead of running
         * wide with a column more.
         */
        public static VisualElement Column(NewGameModePanel panel, out ScrollView list, out Label about)
        {
            VisualElement details = panel._root.Q("ModeDetails");
            Narrow(details);
            // Only the buttons take room, so they line up with the difficulties' beside them
            var column = new VisualElement();
            column.style.alignItems = Align.Center;
            column.style.justifyContent = Justify.Center;
            column.style.position = Position.Relative;
            column.style.marginLeft = 16;
            column.style.marginRight = 16;
            column.style.flexShrink = 0;
            list = new ScrollView(ScrollViewMode.Vertical);
            foreach (string c in ListClasses) list.AddToClassList(c);
            column.Add(list);
            about = new Label();
            about.AddToClassList("new-game-mode-panel__mode-description");
            about.style.whiteSpace = WhiteSpace.Normal;
            about.style.width = DetailsWidth;
            about.style.unityTextAlign = TextAnchor.UpperCenter;
            about.style.marginTop = 10;
            about.style.marginBottom = 4;
            VisualElement under = details?.Q("ModeDescription");
            if (under?.parent != null)
            {
                // Under the lines already there, so they read in the columns' order
                int at = under.parent.IndexOf(under) + 1;
                while (at < under.parent.childCount && under.parent[at] is Label) at++;
                under.parent.Insert(at, about);
            }
            else column.Add(about);
            return column;
        }

        /** A button like the difficulties', for a column's list. */
        public static Button Button(VisualElementLoader loader, string text, Action click)
        {
            var button = (Button)loader.LoadVisualElement(ButtonTemplate);
            button.text = text;
            button.RegisterCallback((ClickEvent _) => click());
            return button;
        }

        private static void Narrow(VisualElement details)
        {
            if (details == null) return;
            details.style.maxWidth = DetailsWidth;
            details.style.flexShrink = 1;
            Label description = details.Q<Label>("ModeDescription");
            if (description == null) return;
            // A centred label takes its text's width unless given one
            description.style.whiteSpace = WhiteSpace.Normal;
            description.style.width = DetailsWidth;
            description.style.unityTextAlign = TextAnchor.UpperCenter;
        }

        // The modes for what the page is making now: a game alone or with others
        internal void Refresh()
        {
            if (_list == null) return;
            bool alone = Modes.PageAlone();
            if (alone != _alone || _list.childCount == 0)
            {
                _alone = alone;
                _list.Clear();
                foreach (GameMode mode in Modes.For(alone))
                {
                    string id = mode.Id;
                    Button button = Button(_visualElementLoader, mode.Label(_loc), () => Modes.Pick(id, alone));
                    button.userData = id;
                    _list.Add(button);
                }
            }
            bool shown = HasChoice(alone);
            _list.parent.ToggleDisplayStyle(shown);
            string picked = Modes.PickedMode(alone);
            foreach (VisualElement button in _list.Children()) button.EnableInClassList(SelectedClass, (string)button.userData == picked);
            string key = Modes.Get(picked)?.DescriptionKey(alone);
            _about.text = key != null ? _loc.T(key) : "";
            _about.ToggleDisplayStyle(shown && _about.text != "");
        }

        private void OnPicked()
        {
            Refresh();
            // The summary names the pick too
            _panel?.UpdateSummary();
        }

        // The summary's "Faction - Map - Difficulty", and the mode
        internal void AddToSummary(NewGameModePanel panel)
        {
            if (panel._summary == null || !panel._visible || !HasChoice(Modes.PageAlone())) return;
            AddToSummary(panel, Modes.Label(_loc, Modes.PickedMode(Modes.PageAlone())));
        }

        /** A pick at the end of the page's summary, "Faction - Map - Difficulty - Pick". */
        public static void AddToSummary(NewGameModePanel panel, string pick)
        {
            if (panel._summary == null) return;
            panel._summary.text += " - " + pick;
            // The capsule behind it is as wide as the game's own summary: it grows to fit
            VisualElement capsule = panel._summary.parent;
            if (capsule != null && capsule.style.width.keyword != StyleKeyword.Auto)
            {
                capsule.style.minWidth = capsule.resolvedStyle.width > 0 ? capsule.resolvedStyle.width : 285;
                capsule.style.width = StyleKeyword.Auto;
                capsule.style.paddingLeft = 16;
                capsule.style.paddingRight = 16;
            }
        }
    }

    [HarmonyPatch(typeof(NewGameModePanel), nameof(NewGameModePanel.GetPanel))]
    class GameModeColumnPanelPatcher
    {
        // Before other mods' columns, so the mode is the first beside the difficulties
        [HarmonyPriority(Priority.High)]
        static void Postfix(NewGameModePanel __instance) => GameModeColumn.Instance?.AddTo(__instance);
    }

    // As the page shows, and whenever what it makes may have changed
    [HarmonyPatch(typeof(NewGameModePanel), nameof(NewGameModePanel.UpdateNextButton))]
    class GameModeColumnRefreshPatcher
    {
        static void Postfix() => GameModeColumn.Instance?.Refresh();
    }

    [HarmonyPatch(typeof(NewGameModePanel), nameof(NewGameModePanel.UpdateSummary))]
    class GameModeColumnSummaryPatcher
    {
        // First, before what other mods add to it
        [HarmonyPriority(Priority.High)]
        static void Postfix(NewGameModePanel __instance) => GameModeColumn.Instance?.AddToSummary(__instance);
    }

    // The page's own Start: a game alone, in the mode picked for it
    [HarmonyPatch(typeof(NewGameModePanel), nameof(NewGameModePanel.StartNewGame))]
    class GameModeStartPatcher
    {
        static void Prefix() => Modes.StartWith(Modes.PickedMode(alone: true), alone: true);
    }
}

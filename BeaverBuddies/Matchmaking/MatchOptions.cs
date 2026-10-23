using System;
using System.Collections.Generic;
using System.Linq;
using BeaverBuddies.Connect;
using BeaverBuddies.Steam;
using GameModes;
using Timberborn.CoreUI;
using Timberborn.GameSceneLoading;
using Timberborn.Localization;
using Timberborn.MainMenuPanels;
using UnityEngine;
using UnityEngine.UIElements;

namespace BeaverBuddies.Matchmaking
{
    /** A choice a mod adds to hosting a new game and finding a match. */
    public class MatchOption
    {
        public string Id;
        public string LabelLocKey;
        public string[] Values;
        public string[] ValueLocKeys;
        // A line about each value, shown under the choice when it's picked; optional
        public Dictionary<string, string> DescriptionLocKeys = new();
        public string Default;

        public string Label(ILoc loc, string value)
        {
            int index = Array.IndexOf(Values, value);
            return index >= 0 && index < ValueLocKeys.Length ? loc.T(ValueLocKeys[index]) : value;
        }
    }

    /**
     * The choices mods add to a new multiplayer game (see ModBridge's
     * AddMatchOption), and its game mode (GameModes.Modes): a column each
     * on the New Game screens' last page when hosting or finding a match.
     * A match only pairs players who picked the same, and the game that
     * starts hands the picks back to the mods (GameValue), as it loads,
     * before it's saved and hosted.
     */
    public static class MatchOptions
    {
        public static readonly List<MatchOption> All = new();

        // The game mode, among the picks under the key it's recorded by
        public const string GameModeId = Modes.RecordKey;
        // BeaverBuddies' own: everyone building one settlement together
        public const string CoOp = "bb.coop";

        public static void AddCoOp()
        {
            Modes.Add(CoOp, "BeaverBuddies.Match.GameMode.CoOp", alone: false);
            Modes.Describe(CoOp, "BeaverBuddies.Match.GameMode.CoOp.Description");
        }

        // Whether an id is one of the picks: an option or the game mode
        public static bool IsPick(string id) => id == GameModeId || All.Any(o => o.Id == id);

        private static readonly Dictionary<string, string> Picked = new();

        // The picks for the game starting on this machine, set as it starts
        private static Dictionary<string, string> _forGame = new();

        public static void Add(MatchOption option)
        {
            All.RemoveAll(o => o.Id == option.Id);
            All.Add(option);
        }

        // What this player picked last time, remembered between games
        public static string ValueOf(string id)
        {
            if (id == GameModeId) return Modes.PickedMode(alone: false);
            MatchOption option = All.FirstOrDefault(o => o.Id == id);
            if (option == null) return null;
            if (!Picked.TryGetValue(id, out string value))
            {
                value = PlayerPrefs.GetString(PrefsKey(id), option.Default);
                Picked[id] = value;
            }
            return option.Values.Contains(value) ? value : option.Default;
        }

        public static void Pick(string id, string value)
        {
            if (id == GameModeId)
            {
                Modes.Pick(value, alone: false);
                return;
            }
            Picked[id] = value;
            PlayerPrefs.SetString(PrefsKey(id), value);
        }

        private static string PrefsKey(string id) => "BeaverBuddies.MatchOption." + id;

        /** Every pick as this player has it set: the game mode and the options. */
        public static Dictionary<string, string> Current()
        {
            Dictionary<string, string> picks = All.ToDictionary(o => o.Id, o => ValueOf(o.Id));
            if (ValueOf(GameModeId) is { } mode) picks[GameModeId] = mode;
            return picks;
        }

        /** The picks as a key a search can match exactly. */
        public static string Key(Dictionary<string, string> options) => options == null ? "" :
            string.Join(";", options.OrderBy(o => o.Key, StringComparer.Ordinal).Select(o => o.Key + "=" + o.Value));

        // Set right before the game is started, so the next new game takes them
        private static bool _armed;

        // A game with others, with these picks: the mode goes to GameModes
        public static void StartGameWith(Dictionary<string, string> options)
        {
            _forGame = options != null ? options.Where(o => o.Key != GameModeId).ToDictionary(o => o.Key, o => o.Value) : new Dictionary<string, string>();
            _armed = true;
            Modes.StartWith(options != null && options.TryGetValue(GameModeId, out string mode) ? mode : null, alone: false);
            Plugin.Log($"Starting a game with options: {Key(options)}");
        }

        // A new game started without picks, or a saved game (which keeps its own), has none
        internal static void Starting(bool newGame)
        {
            if (!newGame || !_armed) _forGame = new Dictionary<string, string>();
            _armed = false;
        }

        /** The pick for the game starting on this machine, or null if it wasn't started with one. */
        public static string GameValue(string id) => id == GameModeId ? Modes.Game : _forGame.TryGetValue(id, out string value) ? value : null;

        // The options the new game starting on this machine was started with, for its record
        internal static IReadOnlyDictionary<string, string> ForGameOptions => _forGame;

        /** The picks by their values alone: Horde, PvP. */
        public static IEnumerable<string> Labels(ILoc loc, Dictionary<string, string> options)
        {
            if (options == null) yield break;
            if (Modes.ModeOf(options) is { } mode) yield return Modes.Label(loc, mode);
            foreach (MatchOption o in All.Where(o => options.ContainsKey(o.Id))) yield return o.Label(loc, options[o.Id]);
        }

        /** The picks in words, one per line: the game mode's, then the options'. */
        public static string Describe(ILoc loc, Dictionary<string, string> options) => string.Join("\n", GameRecord.Describe(loc, options));

        /** The options' lines, "Label: value", for a record. */
        internal static IEnumerable<string> Lines(ILoc loc, IReadOnlyDictionary<string, string> options) =>
            All.Where(o => options.ContainsKey(o.Id)).Select(o => $"{loc.T(o.LabelLocKey)}: {o.Label(loc, options[o.Id])}");
    }

    [HarmonyLib.HarmonyPatch(typeof(GameSceneLoader), nameof(GameSceneLoader.StartNewGame))]
    class MatchOptionsNewGamePatcher
    {
        static void Prefix() => MatchOptions.Starting(newGame: true);
    }

    [HarmonyLib.HarmonyPatch(typeof(GameSceneLoader), nameof(GameSceneLoader.StartNewGameInstantly))]
    class MatchOptionsNewGameInstantlyPatcher
    {
        static void Prefix() => MatchOptions.Starting(newGame: true);
    }

    [HarmonyLib.HarmonyPatch(typeof(GameSceneLoader), nameof(GameSceneLoader.StartSaveGame))]
    class MatchOptionsSaveGamePatcher
    {
        static void Prefix() => MatchOptions.Starting(newGame: false);
    }

    [HarmonyLib.HarmonyPatch(typeof(GameSceneLoader), nameof(GameSceneLoader.StartSaveGameInstantly))]
    class MatchOptionsSaveGameInstantlyPatcher
    {
        static void Prefix() => MatchOptions.Starting(newGame: false);
    }

    /**
     * The options on the New Game screens' last page, each a column of its
     * own between the difficulties and their details, built like the
     * difficulties' column: a scrolling list of the same buttons (so any
     * number of mods can add choices), with a line about the one picked
     * under the difficulty's own, in the details.
     */
    public class MatchOptionsPanel : RegisteredSingleton
    {
        public const string ContainerName = "BeaverBuddiesMatchOptions";
        private readonly VisualElementLoader _visualElementLoader;
        private readonly ILoc _loc;
        // Each column's line about its pick, shown again when what it depends on changes
        private readonly List<Action> _refreshes = new();
        private bool _visible;
        private NewGameModePanel _panel;

        public MatchOptionsPanel(VisualElementLoader visualElementLoader, ILoc loc)
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

        public void AddTo(NewGameModePanel panel)
        {
            _panel = panel;
            AddFriendsOnly(panel);
            if (!MatchOptions.All.Any(o => o.Values.Length > 1) || panel._root.Q(ContainerName) != null) return;
            VisualElement difficulties = panel._root.Q("Modes");
            if (difficulties?.parent == null) return;
            var columns = new VisualElement { name = ContainerName };
            columns.style.flexDirection = FlexDirection.Row;
            columns.style.flexShrink = 0;
            // A choice of one isn't one
            foreach (MatchOption option in MatchOptions.All.Where(o => o.Values.Length > 1)) columns.Add(Column(panel, option));
            // After the game mode's
            VisualElement after = panel._root.Q(GameModeColumn.ContainerName) ?? difficulties;
            after.parent.Insert(after.parent.IndexOf(after) + 1, columns);
        }

        private VisualElement Column(NewGameModePanel panel, MatchOption option)
        {
            VisualElement column = GameModeColumn.Column(panel, out ScrollView list, out Label about);
            var buttons = new List<(Button Button, string Value)>();
            void Show()
            {
                string picked = MatchOptions.ValueOf(option.Id);
                foreach (var (button, value) in buttons) button.EnableInClassList(GameModeColumn.SelectedClass, value == picked);
                about.text = picked != null && option.DescriptionLocKeys.TryGetValue(picked, out string key) ? _loc.T(key) : "";
                // Only with the column
                about.ToggleDisplayStyle(!string.IsNullOrEmpty(about.text) && _visible);
            }
            _refreshes.Add(Show);
            foreach (string value in option.Values)
            {
                Button button = GameModeColumn.Button(_visualElementLoader, option.Label(_loc, value), () =>
                {
                    MatchOptions.Pick(option.Id, value);
                    Show();
                    // The summary under the columns names the pick too
                    _panel?.UpdateSummary();
                });
                list.Add(button);
                buttons.Add((button, value));
            }
            Show();
            return column;
        }

        // Only for a game with others
        public static void Show(NewGameModePanel panel, bool visible)
        {
            panel._root?.Q(ContainerName)?.ToggleDisplayStyle(visible);
            var options = SingletonManager.GetSingleton<MatchOptionsPanel>();
            if (options == null) return;
            options._visible = visible;
            options.Refresh();
        }

        private void Refresh()
        {
            foreach (Action refresh in _refreshes) refresh();
        }

        // The picks at the end of the summary, for a game with others
        internal static void AddToSummary(NewGameModePanel panel)
        {
            var options = SingletonManager.GetSingleton<MatchOptionsPanel>();
            if (options == null || !panel._visible || !options._visible) return;
            foreach (MatchOption option in MatchOptions.All.Where(o => o.Values.Length > 1))
            {
                GameModeColumn.AddToSummary(panel, option.Label(options._loc, MatchOptions.ValueOf(option.Id)));
            }
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(NewGameModePanel), nameof(NewGameModePanel.UpdateSummary))]
    class MatchOptionsSummaryPatcher
    {
        static void Postfix(NewGameModePanel __instance) => MatchOptionsPanel.AddToSummary(__instance);
    }
}

using BeaverBuddies.IO;
using BeaverBuddies.Steam;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Globalization;
using Timberborn.CoreUI;
using Timberborn.GameSaveRepositorySystemUI;
using Timberborn.GameSceneLoading;
using Timberborn.Localization;
using Timberborn.MainMenuPanels;
using Timberborn.NewGameConfigurationSystem;
using Timberborn.SingletonSystem;
using Timberborn.TooltipSystem;
using Timberborn.WebNavigation;
using UnityEngine;
using UnityEngine.UIElements;

namespace BeaverBuddies.Connect
{
    // What the New Game screens were opened for
    public enum NewGameIntent
    {
        Play,
        Host,
        Match,
    }

    /**
     * The mod's one button on the main menu: Multiplayer, which swaps the
     * menu's buttons for the multiplayer ones (host a new or a saved game,
     * join one, a quick match, the list of open matches) and a Back button that brings the menu back.
     * Under them, the mod's Discord: the game's own Discord button, pointed
     * at the mod's server.
     * Hosting a new game goes through the game's New Game screens, whose
     * last button hosts it instead of starting it alone.
     */
    public class MultiplayerMenu : RegisteredSingleton
    {
        private const string MenuButtonName = "MultiplayerButton";
        public const string DiscordUrl = "https://discord.gg/J9u64PRGyC";
        public const string HostButtonName = "HostNewGameButton";

        private readonly ILoc _loc;
        private readonly PanelStack _panelStack;
        private readonly NewGameFactionPanel _newGameFactionPanel;
        private readonly LoadGameBox _loadGameBox;
        private readonly GameSceneLoader _gameSceneLoader;
        private readonly ClientConnectionUI _clientConnectionUI;
        private readonly UrlOpener _urlOpener;
        private readonly ITooltipRegistrar _tooltipRegistrar;

        private VisualElement _container;
        private Button _menuButton;
        private readonly List<Button> _buttons = new();
        private VisualElement _discord;
        // Matchmaking's, only shown when Steam is reachable
        private readonly List<Button> _matchButtons = new();
        private Button _openMatchesButton;
        // Join game: in the list of hosts when there is one, here otherwise
        private Button _joinButton;
        // The menu's own elements and how they showed, while the multiplayer buttons are up
        private readonly Dictionary<VisualElement, StyleEnum<DisplayStyle>> _hidden = new();

        public static NewGameIntent Intent { get; set; }
        // The next main menu opens on the multiplayer buttons (a hosted new game that was cancelled)
        public static bool OpenOnLoad;
        // Whether the Load Game box was opened to host a save, or to play one alone
        public static bool HostingSave { get; private set; }

        static MultiplayerMenu()
        {
            // The game modes offered on the New Game screens: those for a game with others when hosting or matching
            GameModes.Modes.PageAlone = () => Intent == NewGameIntent.Play;
        }

        public MultiplayerMenu(ILoc loc, PanelStack panelStack, NewGameFactionPanel newGameFactionPanel, LoadGameBox loadGameBox,
            GameSceneLoader gameSceneLoader, ClientConnectionUI clientConnectionUI, UrlOpener urlOpener, ITooltipRegistrar tooltipRegistrar)
        {
            _tooltipRegistrar = tooltipRegistrar;
            _loc = loc;
            _panelStack = panelStack;
            _newGameFactionPanel = newGameFactionPanel;
            _loadGameBox = loadGameBox;
            _gameSceneLoader = gameSceneLoader;
            _clientConnectionUI = clientConnectionUI;
            _urlOpener = urlOpener;
        }

        public void AddTo(VisualElement mainMenu)
        {
            if (mainMenu.Q<Button>(MenuButtonName) != null) return;
            Button newGame = mainMenu.Q<Button>("NewGameButton");
            if (newGame == null) return;
            _container = newGame.parent;
            // The game's New Game button is an ordinary game, whatever the multiplayer menu opened last
            newGame.RegisterCallback((ClickEvent _) => Intent = NewGameIntent.Play);
            // and its Load Game button loads a save to play alone
            mainMenu.Q<Button>("LoadGameButton")?.RegisterCallback((ClickEvent _) => HostingSave = false);

            _menuButton = ButtonInserter.DuplicateOrGetButton(mainMenu, "LoadGameButton", MenuButtonName, button =>
            {
                button.text = _loc.T("BeaverBuddies.Menu.Multiplayer");
                button.RegisterCallback((ClickEvent _) => ShowMultiplayer(true));
            });
            string after = MenuButtonName;
            Button Add(string name, string locKey, Action click, string tooltipLocKey = null)
            {
                Button button = ButtonInserter.DuplicateOrGetButton(mainMenu, after, name, b =>
                {
                    b.text = _loc.T(locKey);
                    b.RegisterCallback((ClickEvent _) => click());
                    if (tooltipLocKey != null) GameModes.WrappedTooltip.Register(_tooltipRegistrar, b, () => _loc.T(tooltipLocKey));
                });
                button.ToggleDisplayStyle(false);
                _buttons.Add(button);
                after = name;
                return button;
            }
            Add("HostNewGameMenuButton", "BeaverBuddies.Menu.HostNewGame", () => OpenNewGame(NewGameIntent.Host), "BeaverBuddies.Menu.HostNewGame.Tooltip");
            Add("HostSavedGameButton", "BeaverBuddies.Menu.HostSavedGame", () =>
            {
                HostingSave = true;
                _loadGameBox.Open();
            }, "BeaverBuddies.Menu.HostSavedGame.Tooltip");
            _joinButton = Add("JoinGameButton", "BeaverBuddies.Menu.JoinGame", () => _clientConnectionUI.ShowBox(), "BeaverBuddies.Menu.JoinGame.Tooltip");
#if IS_STEAM
            _matchButtons.Add(Add("FindMatchMenuButton", "BeaverBuddies.Match.FindMatch", () => OpenNewGame(NewGameIntent.Match), "BeaverBuddies.Match.FindMatch.Tooltip"));
            // The list works without Steam too (see MatchList), and has joining by code or address in it
            _openMatchesButton = Add("OpenMatchesMenuButton", "BeaverBuddies.Match.OpenMatches",
                () => SingletonManager.GetSingleton<Matchmaking.HostBrowser>()?.Open(), "BeaverBuddies.Match.OpenMatches.Tooltip");
#endif
            Add("MultiplayerBackButton", "BeaverBuddies.Menu.Back", () => ShowMultiplayer(false));
            _discord = DiscordRow(mainMenu);
            if (_discord != null) _container.Add(_discord);
            if (OpenOnLoad)
            {
                OpenOnLoad = false;
                ShowMultiplayer(true);
            }
        }

        // The game's Discord button, copied to point at the mod's server, under what it is
        private VisualElement DiscordRow(VisualElement mainMenu)
        {
            Button original = mainMenu.Q<Button>("DiscordButton");
            if (original == null) return null;
            var row = new VisualElement { name = "ModDiscord" };
            row.style.alignItems = Align.Center;
            row.style.marginTop = 16;
            var label = new Label(_loc.T("BeaverBuddies.Menu.Discord"));
            // Lettered like the update notes beside the menu
            Label notes = mainMenu.Q<Label>("UpdateInfoText");
            if (notes != null) foreach (string c in notes.GetClasses()) label.AddToClassList(c);
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.marginBottom = 4;
            var button = new Button { name = "ModDiscordButton" };
            foreach (string c in original.GetClasses()) button.AddToClassList(c);
            button.RegisterCallback((ClickEvent _) => _urlOpener.OpenUrl(DiscordUrl));
            row.Add(label);
            row.Add(button);
            // As wide as the buttons above it, so its text wraps rather than widening the menu
            Button back = _container.Q<Button>("MultiplayerBackButton");
            back?.RegisterCallback((GeometryChangedEvent e) => row.style.width = e.newRect.width);
            row.style.width = 0;
            row.style.display = DisplayStyle.None;
            return row;
        }

        private void OpenNewGame(NewGameIntent intent)
        {
            Intent = intent;
            _panelStack.HideAndPush(_newGameFactionPanel);
        }

        // The multiplayer buttons in place of the menu's, or the menu back
        // Told when the main menu switches to the Multiplayer buttons or back, with the buttons' container (see ModBridge)
        internal static readonly List<Action<bool, VisualElement>> Shown = new();

        private void ShowMultiplayer(bool multiplayer)
        {
            if (_container == null) return;
            if (multiplayer)
            {
                _hidden.Clear();
                foreach (VisualElement child in _container.Children())
                {
                    if (_buttons.Contains(child as Button) || child == _discord) continue;
                    _hidden[child] = child.style.display;
                    child.style.display = DisplayStyle.None;
                }
            }
            else
            {
                foreach (var pair in _hidden) pair.Key.style.display = pair.Value;
                _hidden.Clear();
            }
            _discord?.ToggleDisplayStyle(multiplayer);
            foreach (Button button in _buttons) button.ToggleDisplayStyle(multiplayer && (_matchButtons.Contains(button) ? FindMatchAvailable : button != _joinButton || _openMatchesButton == null));
            foreach (var hook in Shown) hook(multiplayer, _container);
        }

        private static bool FindMatchAvailable =>
#if IS_STEAM
            Matchmaking.MatchmakingUI.Available;
#else
            false;
#endif

        /**
         * The New Game screens' last button, opened to host: starts the game
         * named for when it was hosted (hosting saves it first, and a game
         * needs its settlement's name for that), then hosts it once it's up
         * (see NewGameHosting).
         */
        public void AddHostButton(NewGameModePanel panel)
        {
            ButtonInserter.DuplicateOrGetButton(panel._root, "NextButton", HostButtonName, button =>
            {
                button.text = _loc.T("BeaverBuddies.Menu.HostGame");
                button.RegisterCallback((ClickEvent _) => HostNewGame(panel));
            });
        }

        private void HostNewGame(NewGameModePanel panel)
        {
            if (!panel.TryGetValidatedGameMode(out GameModeSpec mode)) return;
            // As the game names autosaves: a settlement's name is its folder, which can't have dots or colons
            string settlement = "Co-op " + DateTime.Now.ToString("yyyy-MM-dd HH'h'mm", CultureInfo.InvariantCulture);
            var configuration = new NewGameConfiguration(panel._factionSpec.Id, panel._map.MapFileReference, mode, settlement);
            HostLobby.NextMapName = panel._map.DisplayName;
#if IS_STEAM
            Matchmaking.HostedListing.HostingNewGame(configuration, panel._map.DisplayName, panel._predefinedGameMode?.DisplayNameLocKey);
#endif
            NewGameHosting.Start(_gameSceneLoader, configuration, Matchmaking.MatchOptions.Current());
        }
    }

    /**
     * A new game started to host: a moment for it to finish setting up,
     * then it's saved and hosted, as a rehost would.
     */
    public class NewGameHosting : IUpdatableSingleton
    {
        private const float SettleTime = 2;

        public static bool Pending;

        /**
         * The new game being set up is the one that's hosted: its server is up before it loads, it's
         * saved once it has settled for the players joining it, and it plays on from there, instead of
         * being loaded again from that save (see ServerHostingUtils.HostInPlace).
         */
        public static bool InPlace { get; private set; }
        // The game hosted in place hasn't started yet: what it does meanwhile is setting up, not played
        public static bool SettingUp { get; set; }

        // Whether the new game about to load is one to host
        private static bool WillHost =>
            Pending
#if IS_STEAM
            || Matchmaking.MatchmakingSession.HostsNewGame
#endif
            ;

        public static void PrepareServer()
        {
            InPlace = false;
            if (!WillHost || !EventIO.IsNull) return;
            var io = new BeaverBuddies.IO.ServerEventIO();
            if (!io.Start(null))
            {
                EventIO.Reset();
                return;
            }
            EventIO.Set(io);
            InPlace = true;
            SettingUp = true;
            ServerHostingUtils.HoldPausedFromLoad();
        }

        // Starts the new game to host it, with the picks next to the difficulties (the game mode)
        public static void Start(GameSceneLoader loader, NewGameConfiguration configuration, Dictionary<string, string> options)
        {
            Plugin.Log($"Hosting a new game: {configuration}");
            Pending = true;
            Matchmaking.MatchOptions.StartGameWith(options);
            loader.StartNewGame(configuration);
        }

        private readonly RehostingService _rehostingService;
        private readonly DialogBoxShower _dialogBoxShower;
        private float _hostAt = -1;
        // Handed over to host: Pending stays up until the hosting box is, so the game that's
        // setting up isn't played in between (see ServerHostingUtils.LoadAndHost)
        private bool _handedOver;

        public NewGameHosting(RehostingService rehostingService, DialogBoxShower dialogBoxShower)
        {
            _rehostingService = rehostingService;
            _dialogBoxShower = dialogBoxShower;
        }

        public void UpdateSingleton()
        {
            if (!Pending)
            {
                _handedOver = false;
                return;
            }
            if (_handedOver) return;
            if (_hostAt < 0)
            {
                _hostAt = Time.realtimeSinceStartup + SettleTime;
                if (InPlace)
                {
                    HostingCover.Show();
                    SingletonManager.GetSingleton<ReplayService>()?.FreezeForHosting();
                }
            }
            if (Time.realtimeSinceStartup < _hostAt) return;
            _handedOver = true;
            if (!(InPlace ? _rehostingService.HostInPlace() : _rehostingService.RehostGame()))
            {
                Pending = false;
                _dialogBoxShower.Create().SetLocalizedMessage("BeaverBuddies.Menu.HostFailed").Show();
            }
        }
    }

    [HarmonyPatch(typeof(GameSceneLoader), nameof(GameSceneLoader.StartNewGame))]
    class NewGameHostingPatcher
    {
        static void Prefix(NewGameConfiguration __0)
        {
            // The map as the player saw it picked, or its file's name
            HostLobby.MapName = HostLobby.NextMapName ?? __0?.MapFileReference.Name;
            HostLobby.NextMapName = null;
            NewGameHosting.PrepareServer();
        }
    }

    [HarmonyPatch(typeof(MainMenuPanel), nameof(MainMenuPanel.GetPanel))]
    public class MainMenuMultiplayerPatcher
    {
        public static void Postfix(VisualElement __result)
        {
            SingletonManager.GetSingleton<MultiplayerMenu>()?.AddTo(__result);
        }
    }

    // The New Game screens' last buttons: added when the panel first shows, when every singleton is up
    [HarmonyPatch(typeof(NewGameModePanel), nameof(NewGameModePanel.GetPanel))]
    public class NewGameModePanelGetPanelPatcher
    {
        public static void Postfix(NewGameModePanel __instance)
        {
            SingletonManager.GetSingleton<Matchmaking.FriendsOnlyPanel>()?.AddTo(__instance);
            SingletonManager.GetSingleton<MultiplayerMenu>()?.AddHostButton(__instance);
#if IS_STEAM
            SingletonManager.GetSingleton<Matchmaking.MatchmakingUI>()?.AddButton(__instance);
#endif
            NewGameModePanelUpdateNextButtonPatcher.Postfix(__instance);
        }
    }

    // Start, Host game or Find a match, by what the screens were opened for,
    // each only when the game could start: with a valid difficulty
    [HarmonyPatch(typeof(NewGameModePanel), nameof(NewGameModePanel.UpdateNextButton))]
    public class NewGameModePanelUpdateNextButtonPatcher
    {
        public static void Postfix(NewGameModePanel __instance)
        {
            if (__instance._root == null) return;
            NewGameIntent intent = MultiplayerMenu.Intent;
#if IS_STEAM
            if (intent == NewGameIntent.Match && !Matchmaking.MatchmakingUI.Available) intent = NewGameIntent.Play;
            Show(__instance, Matchmaking.MatchmakingUI.ButtonName, intent == NewGameIntent.Match);
#else
            if (intent == NewGameIntent.Match) intent = NewGameIntent.Play;
#endif
            Show(__instance, MultiplayerMenu.HostButtonName, intent == NewGameIntent.Host);
            Matchmaking.FriendsOnlyPanel.ShowFriendsOnly(__instance, intent == NewGameIntent.Host);
            __instance._nextButton.ToggleDisplayStyle(intent == NewGameIntent.Play);
        }

        private static void Show(NewGameModePanel panel, string name, bool visible)
        {
            Button button = panel._root.Q<Button>(name);
            if (button == null) return;
            button.ToggleDisplayStyle(visible);
            button.SetEnabled(panel._nextButton.enabledSelf);
        }
    }
}

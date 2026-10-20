using Timberborn.Localization;
using Timberborn.AssetSystem;
using Timberborn.CoreUI;
using UnityEngine;
using UnityEngine.UIElements;

namespace BeaverBuddies.Connect
{
    /**
     * The game's loading background over the map while a new game waits
     * in the hosting box, under the box and above the rest of the UI. The
     * map behind it is only the first of two loads (see ServerHostingUtils).
     * It goes in the dialogs' own root, ahead of them.
     */
    public class HostingCover : RegisteredSingleton
    {
        // Mods that dress the loading screen's picture for their games get the cover to do the same
        internal static readonly System.Collections.Generic.List<System.Action<VisualElement>> Styled = new();

        private readonly IAssetLoader _assetLoader;
        private readonly PanelStack _panelStack;
        private readonly ILoc _loc;
        private readonly Timberborn.MainMenuSceneLoading.MainMenuSceneLoader _mainMenuSceneLoader;
        private VisualElement _cover;

        public HostingCover(IAssetLoader assetLoader, PanelStack panelStack, ILoc loc, Timberborn.MainMenuSceneLoading.MainMenuSceneLoader mainMenuSceneLoader)
        {
            _mainMenuSceneLoader = mainMenuSceneLoader;
            _assetLoader = assetLoader;
            _panelStack = panelStack;
            _loc = loc;
        }

        public static void Show() => SingletonManager.GetSingleton<HostingCover>()?.ShowCover();

        public static void Hide() => SingletonManager.GetSingleton<HostingCover>()?.HideCover();

        // Out of the game, to the main menu's Multiplayer page, without saving it
        public static void Leave()
        {
            Hide();
            MultiplayerMenu.OpenOnLoad = true;
            SingletonManager.GetSingleton<HostingCover>()?._mainMenuSceneLoader.OpenMainMenu();
        }

        private void ShowCover()
        {
            if (_cover != null) return;
            _cover = _assetLoader.Load<VisualTreeAsset>("UI/Views/LoadingScreen/LoadingScreen").Instantiate();
            _cover.style.position = Position.Absolute;
            _cover.style.left = 0;
            _cover.style.right = 0;
            _cover.style.top = 0;
            _cover.style.bottom = 0;
            // The ribbon says where this is, not that anything is loading
            _cover.Q<Label>("LoadingLabel").text = _loc.T("BeaverBuddies.Lobby.Ribbon");
            _cover.Q<VisualElement>("TipWrapper").style.display = DisplayStyle.None;
            // Just ahead of the dialogs' container, after the rest of the UI
            VisualElement container = _panelStack._container;
            container.parent.Insert(container.parent.IndexOf(container), _cover);
            foreach (var style in Styled)
            {
                try { style(_cover); }
                catch (System.Exception e) { Plugin.LogWarning($"A hosting cover style failed: {e}"); }
            }
            Plugin.Log("HostingCover shown");
        }

        private void HideCover()
        {
            _cover?.RemoveFromHierarchy();
            _cover = null;
        }
    }
}

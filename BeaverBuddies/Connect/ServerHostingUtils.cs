using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Timberborn.GameSaveRepositorySystem;
using Timberborn.GameSaveRepositorySystemUI;
using Timberborn.MainMenuScene;
using Timberborn.Localization;
using UnityEngine.UIElements;
using System.Threading.Tasks;
using System.Runtime.InteropServices.ComTypes;
using Newtonsoft.Json.Bson;
using Timberborn.GameSceneLoading;
using BeaverBuddies.IO;
using BeaverBuddies.Util;
using Timberborn.CoreUI;
using System.Collections;
using BeaverBuddies.Steam;
using TimberNet;
using UnityEngine;
using Timberborn.SceneLoading;
using Timberborn.Common;

namespace BeaverBuddies.Connect
{

    [HarmonyPatch(typeof(LoadGameBox), nameof(LoadGameBox.GetPanel))]
    public class LoadGameBoxGetPanelPatcher
    {
        public static void Postfix(LoadGameBox __instance, ref VisualElement __result)
        {
            ILoc _loc = __instance._loc;
            Button host = ButtonInserter.DuplicateOrGetButton(__result, "LoadButton", "HostButton", (button) =>
            {
               button.text = _loc.T("BeaverBuddies.Menu.HostGame");
               button.clicked += () => HostSelectedGame(__instance);
            });
            // From the main menu, one or the other, by which menu opened it: Load Game
            // plays alone, Multiplayer's Host saved game hosts. In a game, both
            bool inMenu = SingletonManager.GetSingleton<MultiplayerMenu>() != null;
            SingletonManager.GetSingleton<Matchmaking.MatchOptionsPanel>()?.AddFriendsOnly(host, !inMenu || MultiplayerMenu.HostingSave);
            if (!inMenu) return;
            host.ToggleDisplayStyle(MultiplayerMenu.HostingSave);
            __result.Q<Button>("LoadButton")?.ToggleDisplayStyle(!MultiplayerMenu.HostingSave);
        }

        [ManualMethodOverwrite]
        /*
         * 04/19/2025
        if (_saveList.TryGetSelectedSave(out var selectedSave))
        {
            if (_gameSaveRepository.SaveExists(selectedSave.SaveReference))
            {
                _validatingGameLoader.LoadGameIfSaveValid(selectedSave.SaveReference);
                return true;
            }

            Debug.LogWarning("Save: " + selectedSave.DisplayName + " doesn't exist, failed to load.");
        }
        return false;
         */
        // Duplicates the LoadGameBox.LoadGame method, but loads the game with
        // the HostingSaveReference instead of ValidatingGameLoader
        private static void HostSelectedGame(LoadGameBox __instance)
        {
            if (__instance._saveList.TryGetSelectedSave(out var selectedSave))
            {
                if (__instance._gameSaveRepository.SaveExists(selectedSave.SaveReference))
                {
                    ServerHostingUtils.LoadIfSaveValidAndHostLoaded(__instance._validatingGameLoader, __instance._dialogBoxShower, selectedSave.SaveReference);
                }
                // Debug.LogWarning("Save: " + selectedSave.DisplayName + " doesn't exist, failed to load.");
            }
        }
    }

    internal class ServerHostingUtils
    {
        public static void LoadIfSaveValidAndHost(ValidatingGameLoader loader, DialogBoxShower shower, SaveReference saveReferece)
        {
            CheckNextValidator(loader, shower, saveReferece, 0, LoadAndHost);
        }

        // The Load game box's Host: the save is loaded for the host to wait in, and again when it starts (see SavedGameHosting)
        public static void LoadIfSaveValidAndHostLoaded(ValidatingGameLoader loader, DialogBoxShower shower, SaveReference saveReferece)
        {
            CheckNextValidator(loader, shower, saveReferece, 0, SavedGameHosting.Begin);
        }

        [ManualMethodOverwrite]
        /*
        04/19/2025 ValidatingGameLoader.CheckNextValidator
        if (index >= _gameLoadValidators.Length)
        {
	        _gameSceneLoader.StartSaveGame(saveReference);
	        return;
        }
        _gameLoadValidators[index].ValidateSave(saveReference, delegate
        {
	        CheckNextValidator(saveReference, index + 1);
        });
         */
        private static void CheckNextValidator(ValidatingGameLoader loader, DialogBoxShower shower, SaveReference saveReference, int index,
            Action<ValidatingGameLoader, DialogBoxShower, SaveReference> host)
        {
            if (index >= loader._gameLoadValidators.Length)
            {
                host(loader, shower, saveReference);
                return;
            }
            loader._gameLoadValidators[index].ValidateSave(saveReference, delegate
            {
                CheckNextValidator(loader, shower, saveReference, index + 1, host);
            });
        }

        private static IEnumerator UpdateDialogBox(DialogBox box, ServerEventIO io, ILoc loc, Action start, HostLobby lobby)
        {
            // Not clickable for a moment: it opens under the cursor, where a
            // double click on Host started the game before anyone could join
            var confirm = box._root.Q<Button>("ConfirmButton");
            confirm?.SetEnabled(false);
            float clickable = UnityEngine.Time.unscaledTime + 1.5f;
            while (true)
            {
                if (confirm != null && !confirm.enabledSelf && UnityEngine.Time.unscaledTime >= clickable) confirm.SetEnabled(true);
                List<string> clients = io.NetBase.GetConnectedClients();
                lobby.Update();
#if IS_STEAM
                // A match's game starts by itself once the other player is in
                Matchmaking.HostedListing.Joined(clients.Count);
                if (Matchmaking.MatchmakingSession.ShouldStartWith(clients.Count))
                {
                    Matchmaking.MatchmakingSession.Finish();
                    box.Close();
                    start();
                    yield break;
                }
#endif
                // So does a test match (see DevTools.TestHarness)
                if (DevTools.TestHarness.HostsMatch && clients.Count > 0)
                {
                    box.Close();
                    start();
                    yield break;
                }
                yield return 0;
            }
        }

        public static byte[] GetMapBtyes(GameSaveRepository repository, SaveReference saveReference)
        {
            var inputStream = repository.OpenSaveWithoutLogging(saveReference);
            byte[] data;
            using (var memoryStream = new MemoryStream())
            {
                inputStream.CopyTo(memoryStream);
                data = memoryStream.ToArray();
            }
            inputStream.Close();
            return data;
        }

        public static MonoBehaviour GetMonoBehaviour(ISceneLoader loader)
        {
            return ((SceneLoader)loader)._coroutineStarter._monoBehaviour;
        }

        /**
         * The game behind the hosting box stays paused until it's closed:
         * a new game being hosted is loaded as one alone, with no
         * ReplayService to freeze it, and nothing may unpause it meanwhile.
         */
        public static bool HoldingPaused { get; private set; }

        // From the moment a game is handed over to host until the host's box is answered: what the game
        // does meanwhile is setting up, not played (see ModBridge.AboutToHost)
        public static bool BoxUp { get; private set; }

        // A game hosted from its first load stays paused from the moment it loads
        public static void HoldPausedFromLoad() => HoldingPaused = true;

        public static void HoldPaused(Timberborn.TimeSystem.SpeedManager speedManager)
        {
            HoldingPaused = true;
            Events.SpeedChangePatcher.SetSpeedSilentlyNow(speedManager, 0);
        }

        /**
         * A new game hosted from its first load: its server has been up since before it loaded
         * (see NewGameHosting.PrepareServer), and now it's saved for those joining. The box asks
         * to start it and the game plays on from where it is, started like a load from the save
         * would be: the same random state.
         */
        public static void HostInPlace(DialogBoxShower shower, GameSaveRepository repository, SaveReference saveReference, MonoBehaviour behavior)
        {
            BoxUp = true;
            NewGameHosting.Pending = false;
            byte[] data = GetMapBtyes(repository, saveReference);
            Plugin.Log($"Reading map with length {data.Length}");
            SingletonManager.GetSingleton<ReplayService>()?.FreezeForHosting();
            var io = (ServerEventIO)EventIO.Get();
            io.SetMap(data);
#if IS_STEAM
            Matchmaking.HostedListing.Hosting(saveReference, data);
            Matchmaking.HostedListing.Publish();
            Matchmaking.MatchmakingSession.ServerStarted();
#endif
            Coroutine coroutine = null;
            var loc = shower._loc;
            Action start = () =>
            {
                HoldingPaused = false;
                BoxUp = false;
                NewGameHosting.SettingUp = false;
                if (coroutine != null) behavior.StopCoroutine(coroutine);
                DeterminismService.ApplyGameStartState(data);
                TEBPatcher.SetHashes(0, 0);
                HostingCover.Hide();
                SingletonManager.GetSingleton<ReplayService>()?.UnfreezeForHosting();
            };
            var boxCreator = shower.Create()
                .SetMessage("")
                .SetConfirmButton(start, loc.T("BeaverBuddies.Host.StartGame"))
                .SetCancelButton(() =>
                {
                    HoldingPaused = false;
                    BoxUp = false;
                    NewGameHosting.SettingUp = false;
                    if (coroutine != null) behavior.StopCoroutine(coroutine);
#if IS_STEAM
                    Matchmaking.HostedListing.Unlist();
#endif
                    if (EventIO.Get() == io) EventIO.Set(new LocalEventIO());
                    else io.Close();
                    // Nothing to play: back to the Multiplayer page it came from
                    HostingCover.Leave();
                }, loc.T(CommonLocKeys.CancelKey));
            var lobby = new HostLobby(shower, io, saveReference.SaveName);
            boxCreator.AddContent(lobby.Build());
            DialogBox box = boxCreator.Show();
            coroutine = behavior.StartCoroutine(UpdateDialogBox(box, io, loc, start, lobby));
        }

        /**
         * A saved game loaded to be hosted (see SavedGameHosting): its server has been up since before
         * it loaded, offering the save as it is, and the box shows over the game it loaded, frozen, as
         * over a new game's. Start loads the save again, from the same seed as every guest, so they
         * all start from the same load.
         */
        public static void HostLoadedSave(DialogBoxShower shower, GameSceneLoader sceneLoader, ServerEventIO io, SaveReference saveReference, byte[] data)
        {
            BoxUp = true;
            SingletonManager.GetSingleton<ReplayService>()?.FreezeForHosting();
            var behavior = GetMonoBehaviour(sceneLoader._sceneLoader);
            Coroutine coroutine = null;
            var loc = shower._loc;
            Action leave = () =>
            {
#if IS_STEAM
                Matchmaking.HostedListing.Unlist();
#endif
                if (EventIO.Get() == io) EventIO.Set(new LocalEventIO());
                else io.Close();
                // Nothing to play: back to the Multiplayer page it came from
                HostingCover.Leave();
            };
            Action start = () =>
            {
                HoldingPaused = false;
                BoxUp = false;
                NewGameHosting.SettingUp = false;
                if (coroutine != null) behavior.StopCoroutine(coroutine);
                // The guests' lobbies stay up until the game this loads is in (see ReplayService.PostLoad)
                io.Stage = ServerEventIO.HostingStage.Loading;
                // What the guests did meanwhile was for no game: not played at its first tick
                io.DiscardPendingClientEvents();
                // Loaded again from its file, which has to be what the guests were sent
                if (!SameSave(sceneLoader._gameSaveRepository, saveReference, data))
                {
                    Plugin.LogError($"{saveReference.SaveName} changed or went since the guests were sent it: not hosted");
                    shower.Create().SetLocalizedMessage("BeaverBuddies.Menu.HostFailed").SetConfirmButton(leave).Show();
                    return;
                }
                // Make sure to set the RNG seed before loading the map
                // The client will do the same
                DeterminismService.InitGameStartState(data);
                sceneLoader.StartSaveGame(saveReference);
            };
            var boxCreator = shower.Create()
                .SetMessage("")
                .SetConfirmButton(start, loc.T("BeaverBuddies.Host.StartGame"))
                .SetCancelButton(() =>
                {
                    HoldingPaused = false;
                    BoxUp = false;
                    NewGameHosting.SettingUp = false;
                    if (coroutine != null) behavior.StopCoroutine(coroutine);
                    leave();
                }, loc.T(CommonLocKeys.CancelKey));
            var lobby = new HostLobby(shower, io, saveReference.SaveName);
            boxCreator.AddContent(lobby.Build());
            DialogBox box = boxCreator.Show();
            coroutine = behavior.StartCoroutine(UpdateDialogBox(box, io, loc, start, lobby));
        }

        // Whether the save is still the one read before, byte for byte
        private static bool SameSave(GameSaveRepository repository, SaveReference saveReference, byte[] data)
        {
            try
            {
                return GetMapBtyes(repository, saveReference).AsSpan().SequenceEqual(data);
            }
            catch (Exception e)
            {
                Plugin.LogWarning($"Couldn't read {saveReference.SaveName} again: {e.Message}");
                return false;
            }
        }

        public static void LoadAndHost(ValidatingGameLoader loader, DialogBoxShower shower, SaveReference saveReference)
        {
            // A new game only set up to be hosted: the map behind the box is
            // loaded again when the host starts, so it's covered meanwhile
            bool newGame = Modding.ModBridge.AboutToHost();
            BoxUp = true;
            NewGameHosting.Pending = false;
            var sceneLoader = loader._gameSceneLoader;
            var repository = sceneLoader._gameSaveRepository;
            byte[] data = GetMapBtyes(repository, saveReference);
            Plugin.Log($"Reading map with length {data.Length}");

            // The game the save was made from stays loaded until the host
            // starts: frozen, so it plays no ticks that a player joining
            // meanwhile would be sent, then find missing from the save. The
            // hosting box's own pause doesn't hold with fewer forced pauses
            SingletonManager.GetSingleton<ReplayService>()?.FreezeForHosting();

#if IS_STEAM
            Matchmaking.HostedListing.Hosting(saveReference, data);
#endif
            ServerEventIO io = new ServerEventIO();
            EventIO.Set(io);
            if (!io.Start(data))
            {
                // Not left as the game's IO with nothing behind it
                EventIO.Reset();
                BoxUp = false;
                shower.Create().SetLocalizedMessage("BeaverBuddies.Menu.HostFailed").Show();
                return;
            }
#if IS_STEAM
            Matchmaking.HostedListing.Publish();
            Matchmaking.MatchmakingSession.ServerStarted();
#endif

            var behavior = GetMonoBehaviour(sceneLoader._sceneLoader);
            Coroutine coroutine = null;

            var loc = shower._loc;
            Action start = () =>
            {
                HoldingPaused = false;
                BoxUp = false;
                if (coroutine != null)
                {
                    behavior.StopCoroutine(coroutine);
                }
                // A hosted game stays listed once it starts: players can join
                // a running game, and it goes off the list when it's full

                // Make sure to set the RNG seed before loading the map
                // The client will do the same
                DeterminismService.InitGameStartState(data);

                sceneLoader.StartSaveGame(saveReference);
            };
            var boxCreator = shower.Create()
                .SetMessage("")
                .SetConfirmButton(start, loc.T("BeaverBuddies.Host.StartGame"))
                .SetCancelButton(() =>
                {
                    HoldingPaused = false;
                    BoxUp = false;
                    if (coroutine != null)
                    {
                        behavior.StopCoroutine(coroutine);
                    }
#if IS_STEAM
                    Matchmaking.HostedListing.Unlist();
#endif
                    // The game behind the box plays on, alone, instead of
                    // sitting frozen with events (the options menu's among
                    // them) recorded and never replayed
                    if (EventIO.Get() == io) EventIO.Set(new LocalEventIO());
                    else io.Close();
                    if (newGame)
                    {
                        // Nothing to play: back to the Multiplayer page it came from
                        HostingCover.Leave();
                        return;
                    }
                    SingletonManager.GetSingleton<ReplayService>()?.UnfreezeForHosting();
                }, loc.T(CommonLocKeys.CancelKey));
            var lobby = new HostLobby(shower, io, saveReference.SaveName);
            boxCreator.AddContent(lobby.Build());
            DialogBox box = boxCreator.Show();
            coroutine = behavior.StartCoroutine(UpdateDialogBox(box, io, shower._loc, start, lobby));

        }
    }
}

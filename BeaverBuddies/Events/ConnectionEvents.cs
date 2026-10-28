using BeaverBuddies.Connect;
using BeaverBuddies.DevTools;
using BeaverBuddies.IO;
using BeaverBuddies.Util;
using System;
using Timberborn.CoreUI;
using Timberborn.Localization;
using Timberborn.Versioning;
using Timberborn.WebNavigation;
using UnityEngine.UIElements;

namespace BeaverBuddies.Events
{
    [Serializable]
    public class InitializeClientEvent : ReplayEvent
    {
        public string serverModVersion;
        public string serverGameVersion;
        //public string mapName;
        public bool isDebugMode;
        // Defaults to true for hosts that don't send it
        public bool allowDevTools = true;
        // The host's add-on mods; null from hosts that don't send it
        public string serverAddOns;
        // The host's factions, other mods' too; null from hosts that don't send it
        public string serverFactions;
        // The host's game hasn't been started yet: the host is in the box, and the map is of this name
        public bool hostWaiting;
        public string hostMapName;
        // What the host needs a player to have alike, and its build (see Connect.BuildIdentity); null from hosts that
        // don't send them. A player checks them before it loads the map (see ClientConnectionService)
        public string serverCompat;
        public string serverBuild;
        // What pauses everyone, as the host chose it for every player (see Settings.PauseReductionSetting); null from hosts that don't send it
        public int? pauses;
        // How much of the game's slowing of fast speeds in big colonies is lifted, as the host chose it for every player
        // (see Settings.SpeedThrottleSetting); null from hosts that don't send it
        public int? throttle;

        public override void Replay(IReplayContext context)
        {
            //context.GetSingleton<ReplayService>().SetServerMapName(mapName);
            DevToolsPolicy.HostAllowsDevTools = allowDevTools;
            if (pauses is int hostPauses && EventIO.Get() is ClientEventIO client) client.HostPauses = hostPauses;
            if (throttle is int hostThrottle && EventIO.Get() is ClientEventIO guest) guest.HostThrottle = hostThrottle;
            if (hostWaiting) Connect.GuestLobby.Open(hostMapName);
            Plugin.Log($"Host allows dev tools: {allowDevTools}");
            string warningMessage = null;
            if (serverGameVersion != GameVersions.CurrentVersion.ToString())
            {
                warningMessage = $"Warning! Server Timberborn version ({serverGameVersion}) does not match client Timberborn version ({GameVersions.CurrentVersion}).\n" +
                    $"Please ensure that you are running the same version of the game.";
            } else if (serverModVersion != Plugin.Version)
            {
                warningMessage = $"Warning! Server mod version ({serverModVersion}) does not match client mod version ({Plugin.Version}).\n" +
                    $"Please ensure that you are running the same version of the {Plugin.ID} mod.";
            } else if (serverBuild != null && serverBuild != Connect.BuildIdentity.Build)
            {
                warningMessage = $"Warning! Server mod build ({serverModVersion}, build {serverBuild}) does not match client mod build ({Connect.BuildIdentity.Describe}).\n" +
                    $"Please ensure that you are running the same build of the {Plugin.ID} mod, from the same download, or the game may fall out of sync.";
            } else if (serverAddOns != null && serverAddOns != ReplayHooks.AddOnList)
            {
                warningMessage = $"Warning! The host plays with these add-on mods: {Describe(serverAddOns)}.\n" +
                    $"You play with: {Describe(ReplayHooks.AddOnList)}.\n" +
                    $"Please install the same mods, at the same versions, or the game will fall out of sync.";
            } else if (serverFactions != null && serverFactions != ReplayHooks.FactionList)
            {
                warningMessage = $"Warning! The host's game has these factions: {Describe(serverFactions)}.\n" +
                    $"Yours has: {Describe(ReplayHooks.FactionList)}.\n" +
                    $"Please install the same faction mods, at the same versions, or the game won't load or will fall out of sync.";
            }
            // Debug mode only changes how desyncs are caught: both ends need it
            // on for a detailed desync report, but play the same either way
            if (isDebugMode != Settings.Debug)
            {
                Plugin.LogWarning($"Host debug mode ({isDebugMode}) differs from ours ({Settings.Debug}): " +
                    "a traced desync report needs desync tracing on for both players");
            }
            if (warningMessage != null)
            {
                Plugin.LogWarning(warningMessage);
                context.GetSingleton<DialogBoxShower>().Create().SetMessage(warningMessage).Show();
            }
        }

        // Short, so it fits in the warning and the desync report
        public static readonly string ModBuild = typeof(Plugin).Assembly.ManifestModule.ModuleVersionId.ToString("N").Substring(0, 8);

        private static string Describe(string addOns) => string.IsNullOrEmpty(addOns) ? "none" : addOns;

        public static InitializeClientEvent Create()
        {
            InitializeClientEvent message = new InitializeClientEvent()
            {
                serverModVersion = Plugin.Version,
                serverGameVersion = GameVersions.CurrentVersion.ToString(),
                isDebugMode = Settings.Debug,
                allowDevTools = Settings.AllowDevTools,
                serverAddOns = ReplayHooks.AddOnList,
                serverFactions = ReplayHooks.FactionList,
                hostWaiting = Connect.NewGameHosting.SettingUp,
                hostMapName = Connect.HostLobby.MapName,
                serverCompat = Connect.BuildIdentity.CompatValue,
                serverBuild = Connect.BuildIdentity.Build,
                pauses = (int)Settings.PauseReductionSetting,
                throttle = Settings.SpeedThrottleSetting,
            };
            return message;
        }
    }

    [Serializable]
    public class ClientDesyncedEvent : ReplayEvent
    {
        public string desyncID;
        public string desyncTrace;
        // Which entities and water tiles differed, on the first tick they did
        // (see StateDigest): the buckets and squares, and our side of them
        public int detailTick;
        public int[] detailEntityBuckets;
        public int[] detailWaterAreas;
        public string[] detailEntities;
        public string[] detailWater;
        // Where those entities were over the last ticks, and what the traces said about them
        public string[] detailHistory;
        public string[] detailTraces;
        // Without tracing: this machine's buckets, their counts and its walkers at the tick its hashes parted from the
        // host's, and the one before, for the host to say which walker it was (see StateDigest.Snapshot)
        public int snapshotTick;
        public int[] snapshotBuckets;
        public int[] snapshotCounts;
        public string[] snapshotWalkers;
        // Only for the host to say what differs, in the test harness, which keeps a desync as it is
        public bool analyzeOnly;
        // What the player's machine found apart from the host's, when it knew (see ReplayService.HandleDesync)
        public string reason;

        public override string ToActionString() => reason == null ? "Out of sync" : $"Out of sync: {reason}";

        /**
         * The mod's Discord open, where players report a desync with every
         * player's Player.log; with tracing on, the trace of it saved next to
         * the game's log first, to post too.
         */
        private void ReportOnDiscord(IReplayContext context, Action<string> callback)
        {
            if (Settings.Debug)
            {
                try
                {
                    string folder = System.IO.Path.GetDirectoryName(UnityEngine.Application.consoleLogPath);
                    string file = System.IO.Path.Combine(folder, $"BeaverBuddies-desync-{desyncID}.txt");
                    string why = reason == null ? "" : $"Out of sync: {reason}\n";
                    System.IO.File.WriteAllText(file, $"BeaverBuddies: {Plugin.Version} (build {InitializeClientEvent.ModBuild}); Timberborn: {GameVersions.CurrentVersion}\n{why}\n{desyncTrace}");
                    Plugin.Log($"Desync trace saved to {file}");
                }
                catch (Exception e)
                {
                    Plugin.LogError($"Couldn't save the desync trace: {e.Message}");
                }
            }
            // Test copies don't open browser tabs on the Mac they run on
            if (DevTools.TestHarness.Active) Plugin.Log($"Would open {MultiplayerMenu.DiscordUrl}");
            else context.GetSingleton<UrlOpener>()?.OpenUrl(MultiplayerMenu.DiscordUrl);
            callback(Settings.Debug ? "BeaverBuddies.ClientDesynced.TraceSaved" : "BeaverBuddies.ClientDesynced.PostLogs");
        }

        private static void TurnOnTracing(IReplayContext context)
        {
            // For everyone, on the same tick (see TracingEnabledEvent)
            if (DoPrefix(() => new DesyncDetecter.TracingEnabledEvent())) new DesyncDetecter.TracingEnabledEvent().Replay(context);
        }

        /**
         * Turning on tracing, a button of its own beside the report's, looking
         * as it does: the box has only one button of that kind, and its cancel
         * button is what Escape presses, which mustn't turn tracing on for
         * everyone.
         */
        private static void AddTracingButton(IReplayContext context, ILoc loc, Button infoButton)
        {
            // Of the report button's own kind, the game's, which draws its frame itself: its classes alone don't
            var button = (Button)Activator.CreateInstance(infoButton.GetType());
            button.name = "TracingButton";
            button.text = loc.T("BeaverBuddies.ClientDesynced.EnableTracing");
            foreach (string style in infoButton.GetClasses()) button.AddToClassList(style);
            button.clicked += () =>
            {
                button.SetEnabled(false);
                button.text = loc.T("BeaverBuddies.ClientDesynced.TracingEnabled");
                TurnOnTracing(context);
            };
            infoButton.parent.Insert(infoButton.parent.IndexOf(infoButton) + 1, button);
        }

        public override void Replay(IReplayContext context)
        {
            if (analyzeOnly && !(EventIO.Get() is ServerEventIO)) return;
            ReplayService replayService = context.GetSingleton<ReplayService>();
            // The speed it was at, before this pause, for what the game does once everyone is back (see Connect.PlayOn)
            ResyncService.NoteSpeedBeforeResync(replayService.TargetSpeed);
            replayService.SetTargetSpeed(0);
            ResyncService resyncService = context.GetSingleton<ResyncService>();
            // The host reloads everyone's game from its save, and the report
            // is offered once it's back (see ShowResynced)
            if (EventIO.Get() is ServerEventIO)
            {
                // The host still has the tick it happened at: which entities and tiles differ
                if (detailTick > 0)
                {
                    string analysis = DesyncDetecter.StateDigest.Analyze(detailTick, playerID, detailEntityBuckets, detailEntities, detailWaterAreas, detailWater, detailHistory, detailTraces);
                    Plugin.LogError(analysis);
                    desyncTrace = (desyncTrace ?? "") + analysis;
                }
                // ...and without tracing, which walkers
                if (snapshotBuckets != null)
                {
                    string analysis = DesyncDetecter.StateDigest.AnalyzeSnapshot(snapshotTick, playerID, snapshotBuckets, snapshotCounts, snapshotWalkers);
                    Plugin.LogError(analysis);
                    desyncTrace = (desyncTrace ?? "") + analysis;
                }
                if (analyzeOnly) return;
                resyncService.StartAsHost(this);
                return;
            }
            resyncService.WaitForHost(this);
            context.GetSingleton<DialogBoxShower>().Create()
                .SetLocalizedMessage("BeaverBuddies.ClientDesynced.Resyncing")
                .Show();
        }

        /**
         * Once the resync's save has loaded: the game's back in sync, and
         * the desync can be reported.
         */
        public void ShowResynced(IReplayContext context)
        {
            var shower = context.GetSingleton<DialogBoxShower>();
            ILoc _loc = shower._loc;
            Button infoButton = null;

            Action<string> infoCallback = (message) =>
            {
                if (infoButton != null)
                {
                    infoButton.text = _loc.T(message);
                }
            };
            Action bugReportAction = () =>
            {
                infoButton?.SetEnabled(false);
                ReportOnDiscord(context, infoCallback);
            };

            string message = _loc.T("BeaverBuddies.ClientDesynced.Resynced");
            // Without tracing, every player's log is the report, and tracing can be turned on for the next one
            if (!Settings.Debug)
            {
                message += "\n\n" + _loc.T("BeaverBuddies.ClientDesynced.NeedToEnableTracing")
                    + "\n\n" + _loc.T("BeaverBuddies.ClientDesynced.TracingSideEffect");
            }

            DialogBox box = shower.Create().SetMessage(message)
                .SetInfoButton(bugReportAction, _loc.T("BeaverBuddies.ClientDesynced.ReportOnDiscord"))
                .Show();
            infoButton = box.GetPanel().Q<Button>("InfoButton");
            if (!Settings.Debug && infoButton != null) AddTracingButton(context, _loc, infoButton);
        }
    }

    /*
     * A player's connection ended, as the host saw it: what they held is let
     * go of for them, on every machine at this event's tick, as their own
     * let-go won't come. Played after whatever they sent before they went.
     */
    public class PlayerLeftEvent : ReplayEvent
    {
        public string leftID;

        public override void Replay(IReplayContext context) => LeverHolders.LetGoAll(context, leftID);

        public override string ToActionString()
        {
            return $"Player {leftID} left";
        }
    }
}

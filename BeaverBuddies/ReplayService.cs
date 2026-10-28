// Define to force game to run a full tick each
// update, rather than amortizing ticks over multiple.
//#define ONE_TICK_PER_UPDATE

using BeaverBuddies.Connect;
using BeaverBuddies.DevTools;
using BeaverBuddies.DesyncDetecter;
using BeaverBuddies.Events;
using BeaverBuddies.IO;
using BeaverBuddies.Performance;
using HarmonyLib;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Timberborn.Automation;
using Timberborn.Autosaving;
using Timberborn.BlockObjectTools;
using Timberborn.Buildings;
using Timberborn.CoreUI;
using Timberborn.DemolishingUI;
using Timberborn.EntitySystem;
using Timberborn.Forestry;
using Timberborn.GameSaveRepositorySystem;
using Timberborn.GameSaveRuntimeSystem;
using Timberborn.GameWonderCompletion;
using Timberborn.Options;
using Timberborn.PlantingUI;
using Timberborn.ScienceSystem;
using Timberborn.SingletonSystem;
using Timberborn.TemplateInstantiation;
using Timberborn.TickSystem;
using Timberborn.TimeSystem;
using Timberborn.WebNavigation;
using Timberborn.Workshops;
using Timberborn.WorkSystem;
using Timberborn.WorkSystemUI;
using Timberborn.ZiplineSystem;
using static BeaverBuddies.SingletonManager;
using static Timberborn.TickSystem.TickableSingletonService;

namespace BeaverBuddies
{
    public interface IEarlyTickableSingleton : ITickableSingleton
    {
    }

    /**
     * Represents a group of events that should be sent
     * and received together, to ensure all events for a tick
     * are present before they are played.
     */
    class GroupedEvent : ReplayEvent
    {
        public List<ReplayEvent> events;

        public GroupedEvent(List<ReplayEvent> events)
        {
            // Make a copy
            this.events = events.ToList();
        }

        public override void Replay(IReplayContext context)
        {
            throw new NotImplementedException("Do not directly replay grouped events");
        }
    }

    class HeartbeatEvent : ReplayEvent
    {
        // The host's order, move and clock hashes at the start of the tick, for a guest to check its own against
        // (see DesyncDetecterService.DiffersFromHost)
        public int[] hashes;

        public override void Replay(IReplayContext context)
        {
            if (EventIO.Get() is ClientEventIO) DesyncDetecterService.ReceiveHostHashes(ticksSinceLoad, hashes);
        }
    }

    public class ReplayService : RegisteredSingleton, IReplayContext, IPostLoadableSingleton, IUpdatableSingleton, IResettableSingleton
    {
        //private readonly TickWathcerService _tickWathcerService;
        private readonly EventBus _eventBus;
        private readonly SpeedManager _speedManager;
        private readonly GameSaver _gameSaver;
        private readonly ISingletonRepository _singletonRepository;
        private readonly TickingService _tickingService;
        private readonly DeterminismService _determinismService;
        private readonly ResyncService _resyncService;

        private readonly GameSaveHelper gameSaveHelper;

        // A client's box for a host gone quiet, made the first time it's needed
        private HostSilence _hostSilence;

        private List<object> singletons = new();

        // TODO: I believe that this could be a non-static variable
        // set at initialization time, which would prevent accidentally
        // accessing a new game's event IO.
        private EventIO io => EventIO.Get();

        private int __ticksSinceLoad = 0;
        private int ticksSinceLoad 
        { 
            get => __ticksSinceLoad;
            set
            {
                __ticksSinceLoad = value;
                TimeTimePatcher.SetTicksSinceLoaded(value);
                DesyncDetecterService.StartTick(value);
            }
        }
        public int TicksSinceLoad => ticksSinceLoad;

        public float TargetSpeed  { get; private set; } = 0;

        // A client keeps this little behind the host, in seconds: the host's
        // next tick should have been here about this long when the client
        // reaches it. It plays a little faster or slower (between Slowest and
        // Fastest times the buckets) to stay there. Running into a tick that
        // hasn't come yet stops the game until it does, a visible stutter;
        // left alone, it settles a whole tick behind (0.6 s at normal
        // speed), which every action waits on the way back
        private const float ClientCushion = 0.1f, Slowest = 0.85f, Fastest = 1.25f;
        // How much faster per second of margin off the cushion
        private const float PaceGain = 2.5f;
        // When each of the host's ticks that hasn't played yet was first seen
        // here (realtime), and the last one seen. The margin is how long the
        // tick starting had been here. Looking only for the next tick, as it
        // did, it saw one that came while the tick before it still waited as
        // coming when that one began, so the margin was never more than a
        // tick: above speed 6 a tick is shorter than the cushion, and the
        // client, its margin ever short, slowed itself until the coarser rule
        // held it (more ticks behind than the speed), 0.6 s behind the host
        // at speed 7. Every one of its actions waited that long
        private readonly Queue<(int tick, float at)> _arrivals = new();
        private int _lastTickSeen = -1;
        // The client's margin at the last few tick starts (averaged), and
        // when it began waiting for a tick that hadn't come
        private float _margin = ClientCushion;
        private float _waitingSince = -1;
        // How many times the buckets the game asks for a client plays a frame
        public float BucketBoost { get; private set; } = 1;
        public float Margin => _margin;
        private bool _desyncKept;

        public bool IsDesynced { get; private set; } = false;

        private ConcurrentQueue<ReplayEvent> eventsToSend = new ConcurrentQueue<ReplayEvent>();
        private ConcurrentQueue<ReplayEvent> eventsToPlay = new ConcurrentQueue<ReplayEvent>();

        /// <summary>
        /// Returns true if the ReplayService is ready for the game to
        /// start a new a tick, and false if not (e.g. because the client
        /// has not yet received a heartbeat from the server for the next tick).
        /// </summary>
        public bool IsReadyToStartTick {
            get
            {
                // The client shouldn't tick until the server has sent a heartbeat
                // Check the *next* tick, since current tick has already happened
                return !(io is ClientEventIO && !io.HasEventsForTick(TicksSinceLoad + 1));
            }
        }

        public static bool IsLoaded { get; private set; } = false;
        private bool isReset = false;

        // Nothing more plays in a game a reload is replacing: it wouldn't
        // be in the save
        private bool CanAct => io != null && !isReset && !IsDesynced && !_resyncService.IsReloading;

        public static bool IsReplayingEvents { get; private set; } = false;

        public string ServerMapName => GetSingleton<MapNameService>().Name;

        public void Reset()
        {
            Plugin.Log("Resetting Replay Service...");
            IsLoaded = false;
            IsReplayingEvents = false;
            isReset = true;
        }

        public ReplayService(
            EventBus eventBus,
            SpeedManager speedManager,
            GameSaver gameSaver,
            ISingletonRepository singletonRepository,
            TickingService tickingService,
            DeterminismService determinismService,
            BlockObjectPlacerService blockObjectPlacerService,
            BuildingService buildingService,
            PlantingSelectionService plantingSelectionService,
            TreeCuttingArea treeCuttingArea,
            EntityRegistry entityRegistry,
            EntityService entityService,
            RecipeSpecService recipeSpecificationService,
            DemolishableSelectionTool demolishableSelectionTool,
            DemolishableUnselectionTool demolishableUnselectionTool,
            BuildingUnlockingService buildingUnlockingService,
            WorkingHoursManager workingHoursManager,
            WorkingHoursPanel workingHoursPanel,
            WorkplaceUnlockingService workplaceUnlockingService,
            IOptionsBox optionsBox,
            DialogBoxShower dialogBoxShower,
            UrlOpener urlOpener,
            RehostingService rehostingService,
            ResyncService resyncService,
            GameSaveRepository gameSaveRepository,
            MapNameService mapNameService,
            Autosaver autosaver,
            ZiplineConnectionService ziplineConnectionService,
            Settings settings,
            TemplateInstantiator templateInstantiator,
            AutomationResetter automationResetter,
            Timberborn.BlockSystem.BlockValidator blockValidator,
            Timberborn.MainMenuSceneLoading.MainMenuSceneLoader mainMenuSceneLoader
        )
        {
            DesyncDetecter.ThreadSafeWaterMapUpdateDataPatcher.Reset();
            // The game's clock starts again with each load: one left at the
            // last game's time differs between machines that played it for
            // different lengths, as a desynced one and the host after a resync
            TimeTimePatcher.SetTicksSinceLoaded(0);
            //_tickWathcerService = AddSingleton(tickWathcerService);
            _eventBus = AddSingleton(eventBus);
            _speedManager = AddSingleton(speedManager);
            _gameSaver = AddSingleton(gameSaver);
            _singletonRepository = AddSingleton(singletonRepository);
            _tickingService = AddSingleton(tickingService);
            _determinismService = AddSingleton(determinismService);
            AddSingleton(blockObjectPlacerService);
            AddSingleton(buildingService);
            AddSingleton(plantingSelectionService);
            AddSingleton(treeCuttingArea);
            AddSingleton(entityRegistry);
            AddSingleton(entityService);
            AddSingleton(recipeSpecificationService);
            AddSingleton(demolishableSelectionTool);
            AddSingleton(demolishableUnselectionTool);
            AddSingleton(buildingUnlockingService);
            AddSingleton(workingHoursManager);
            AddSingleton(workingHoursPanel);
            AddSingleton(workplaceUnlockingService);
            AddSingleton(optionsBox);
            AddSingleton(dialogBoxShower);
            AddSingleton(urlOpener);
            AddSingleton(rehostingService);
            _resyncService = AddSingleton(resyncService);
            AddSingleton(gameSaveRepository);
            AddSingleton(mapNameService);
            AddSingleton(autosaver);
            AddSingleton(ziplineConnectionService);
            AddSingleton(settings);
            AddSingleton(templateInstantiator);
            AddSingleton(automationResetter);
            AddSingleton(blockValidator);
            AddSingleton(mainMenuSceneLoader);

            AddSingleton(this);

            _eventBus.Register(this);

            gameSaveHelper = new GameSaveHelper(gameSaver);

            _tickingService.replayService = this;
        }

        public void SetTicksSinceLoad(int ticks)
        {
            Plugin.Log($"Setting ticks since load to: {ticks}");
            ticksSinceLoad = ticks;
        }

        public void PostLoad()
        {
            // The tick hashes are for this game: the one loaded before may have ticked
            // (a new game before it's hosted), and a running total from it never matches the others'
            TEBPatcher.SetHashes(0, 0);
            Plugin.Log("PostLoad");
            DeterminismService.ReseedAfterLoad();
            Fixes.DeferredDestroyFix.Reset();
            Fixes.MenuReleaseFix.GameLoaded();
            DesyncDetecter.TickLog.Forget();
            if (DevTools.TestHarness.Active) Util.ObjectCensus.Log("after loading");
            if (HitchWatch.Profiling)
            {
                Util.LeakFinder.Loaded();
                Plugin.LogWarning($"[Pace] heap after loading, collected: {GC.GetTotalMemory(true) >> 20} MB, " +
                    $"Mono used {UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() >> 20} MB of {UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong() >> 20} MB");
            }
            Performance.ActionLatency.Hook();
            _determinismService.UnityThread = Thread.CurrentThread;
            // Tracing carried into this game, always on or turned on before a reload: said on screen, as it stutters
            if (Settings.Debug && (io is ServerEventIO || io is ClientEventIO)) DesyncDetecterService.SayTracingIsOn(this);
            // A saved game hosted by loading it twice is in: its guests' lobbies close (see Connect.SavedGameHosting).
            // The guests waiting there set their random state to the start's again as the first events play, which
            // this load has drawn from since (a host that starts its game where it is, a new game's, sets it as it
            // starts): this host does as they do, at the same events, so all start alike
            if (io is ServerEventIO server && server.Stage == ServerEventIO.HostingStage.Loading)
            {
                server.Stage = ServerEventIO.HostingStage.Running;
                DeterminismService.ReseedAtFirstEvents();
            }
        }

        private T AddSingleton<T>(T singleton)
        {
            singletons.Add(singleton);
            return singleton;
        }

        public T GetSingleton<T>()
        {
            foreach (object singleton in singletons)
            {
                if (singleton is T)
                    return (T)singleton;
            }
            // This doesn't work for some singletons, like GetSingletons<T>(),
            // so we still have to add them manually.
            var result = _singletonRepository.GetSingletons<T>().FirstOrDefault();
            Plugin.Log($"Searching for unregistered singleton {typeof(T)}; found = {result != null}");
            // Kept, so the next time is the list's: some are asked for every tick
            if (result != null) singletons.Add(result);
            return result;
        }

        public void RecordEvent(ReplayEvent replayEvent)
        {
            // During a replay, we save things manually, only if they're
            // successful.
            if (IsReplayingEvents) return;
            if (!IsLoaded) return;
            replayEvent.playerID ??= ReplayEvent.ActingPlayerID ?? Players.PlayerIdentity.LocalID;
            if (!ReplayHooks.AllowSend(replayEvent)) return;
            // A guest in the host's lobby has no game to act in yet: what it did would play when the host
            // starts. Mods' own events still go, for what they do in the lobby
            if (EventIO.Get() is ClientEventIO && Connect.GuestLobby.IsOpen && replayEvent is not Modding.ModEvent)
            {
                Plugin.Log($"Not sending {replayEvent.type}: the host hasn't started the game");
                return;
            }
            Performance.ActionLatency.Stamp(replayEvent);

            string json = JsonSettings.Serialize(replayEvent);
            Plugin.Log($"RecordEvent: {json}");

            UserEventBehavior behavior = UserEventBehavior.Send;
            EventIO io = EventIO.Get();
            if (io != null)
            {
                behavior = io.UserEventBehavior;
            }
            if (behavior == UserEventBehavior.QueuePlay)
            {
                eventsToPlay.Enqueue(replayEvent);
            }
            else
            {
                EnqueueEventForSending(replayEvent);
            }
        }

        private List<ReplayEvent> ReadEventsFromIO(int tick)
        {
            List<ReplayEvent> eventsToReplay = io.ReadEvents(tick);
            // Spread grouped events into a flat list because we need to
            // replay each individually (since we only record them if successful).
            eventsToReplay = eventsToReplay
                .SelectMany(e => e is GroupedEvent grouped ? grouped.events.ToArray() : new ReplayEvent[] { e })
                .ToList();
            return eventsToReplay;
        }

        // More than the few ticks a guest's action takes to reach the host
        private const int LateTicks = 30;

        // Another player's action as it plays here, so each player's log says what everyone did; this player's own
        // are logged as they're recorded
        private static void LogOthersAction(ReplayEvent replayEvent, int eventTime, int currentTick)
        {
            string player = replayEvent.playerID;
            if (player == null || player == Players.PlayerIdentity.LocalID || replayEvent is HeartbeatEvent) return;
            string action;
            // Only said, so it mustn't stop the action from playing
            try { action = replayEvent is Modding.ModEvent modEvent ? modEvent.mod : replayEvent.ToActionString(); }
            catch (Exception e) { action = $"(can't say: {e.Message})"; }
            string sent = eventTime < currentTick ? $" (sent at {eventTime})" : "";
            Plugin.Log($"Playing {replayEvent.type} by {player.Substring(0, Math.Min(8, player.Length))} at tick {currentTick}{sent}: {action}");
        }

        private void ReplayEvents()
        {
            if (_tickingService.NextBucket != 0)
            {
                Plugin.LogWarning($"Warning, replaying events when bucket != 0: {_tickingService.NextBucket}");
            }

            // Start with any events received from connected users
            List<ReplayEvent> eventsToReplay = ReadEventsFromIO(TicksSinceLoad);
            // Then add any from this user that have been deferred
            while (eventsToPlay.TryDequeue(out ReplayEvent replayEvent))
            {
                replayEvent.ticksSinceLoad = ticksSinceLoad;
                eventsToReplay.Add(replayEvent);
            }

            int currentTick = ticksSinceLoad;
            IsReplayingEvents = true;
            // Reset however the loop ends, so a failure can't leave every later
            // local action looking like a replay
            try
            {
                for (int i = 0; i < eventsToReplay.Count; i++)
                {
                    ReplayEvent replayEvent = eventsToReplay[i];
                    int eventTime = replayEvent.ticksSinceLoad;
                    if (eventTime > currentTick)
                        break;
                    // A guest's actions reach the host a few ticks after the guest's tick they were made at; far
                    // later, the connection held them up
                    if (eventTime < currentTick - LateTicks)
                    {
                        Plugin.LogWarning($"{replayEvent.type} by {replayEvent.playerID} came {currentTick - eventTime} ticks late");
                    }
                    LogOthersAction(replayEvent, eventTime, currentTick);

                    // Don't play or relay dev events when the host has disabled dev tools
                    if (DevToolsPolicy.ShouldDropReplayedEvent(replayEvent))
                    {
                        Plugin.LogWarning($"Dropping {replayEvent.type}: dev tools are disabled");
                        continue;
                    }

                    if (!ReplayHooks.AllowPlay(replayEvent))
                    {
                        Plugin.LogWarning($"Dropping {replayEvent.type} by {replayEvent.playerID}: refused by a hook");
                        continue;
                    }
                
                    // The first events after a lobby's start find the random state set to the start's (see
                    // DeterminismService.ReseedAtFirstEvents): checked event by event, as the host's welcome, which
                    // marks a guest waiting in its lobby, can come in the same batch as the host's first events, when
                    // the host started as the guest was loading
                    DeterminismService.ReseedIfWaitingForStart();

                    // If this event was played (e.g. on the server) and recorded a 
                    // random state, make sure we're in the same state.
                    // Skip if we're in Debug mode, since we'll get more details
                    // if we look at the full trace.
                    if (!Settings.Debug && replayEvent.randomS0Before != null)
                    {
                        int s0 = UnityEngine.Random.state.s0;
                        int randomS0Before = (int)replayEvent.randomS0Before;
                        if (s0 != randomS0Before)
                        {
                            Plugin.LogWarning($"Random state mismatch: {s0:X8} != {randomS0Before:X8}");
                            HandleDesync($"the random state differs from the host's at tick {currentTick}");
                            break;
                        }
                    }
                    // A failed event isn't sent on, so it mustn't leave the
                    // random numbers moved on this machine alone
                    var randomState = UnityEngine.Random.state;
                    int traceMark = DesyncDetecterService.Mark();
                    try
                    {
                        // For these events, make sure to record s0 beforehand
                        replayEvent.randomS0Before = UnityEngine.Random.state.s0;
                        // Only what every machine plays alike: the client alone plays its
                        // InitializeClientEvent, and a mod's payload can print differently
                        if (Settings.Debug && replayEvent is not HeartbeatEvent && replayEvent is not InitializeClientEvent
                            && !(replayEvent.GetType().Namespace ?? "").Contains("DesyncDetecter"))
                        {
                            string action = replayEvent is Modding.ModEvent modEvent ? modEvent.mod : replayEvent.ToActionString();
                            DesyncDetecterService.Trace($"Playing {replayEvent.type} by {replayEvent.playerID}: {action}", true, true);
                        }
                        using (ReplayHooks.OpenScopes(replayEvent))
                        {
                            replayEvent.Replay(this);
                        }
                        // What it deleted is gone before the next event, on every machine alike
                        Fixes.DeferredDestroyFix.Flush();
                        // Only send the event if it played successfully and
                        // the IO says we shouldn't skip recording
                        if (!EventIO.SkipRecording)
                        {
                            EnqueueEventForSending(replayEvent);
                        }
                    } catch (Exception e)
                    {
                        UnityEngine.Random.state = randomState;
                        DesyncDetecterService.Rewind(traceMark);
                        Plugin.LogError($"Failed to replay event: {e}");
                        Plugin.LogError(e.ToString());
                    }
                }
            }
            finally
            {
                // Also when an event threw halfway
                Fixes.DeferredDestroyFix.Flush();
                IsReplayingEvents = false;
            }
        }

        // The day clock in one number: the day and the ticks into it. The game compares times against it, and a tick's difference moves when a job ends
        private int DayClock()
        {
            var cycle = this.GetSingleton<Timberborn.TimeSystem.IDayNightCycle>() as Timberborn.TimeSystem.DayNightCycle;
            return cycle == null ? 0 : unchecked(cycle.DayNumber * 100003 + cycle._ticksPassedToday);
        }

        // Nothing can play without the host, so the guest's game ends with it
        private void ShowHostLeft()
        {
            Plugin.Log("The host left: back to the main menu");
            _hostSilence?.Close();
            EventIO.Reset();
            SpeedChangePatcher.SetSpeedSilentlyNow(_speedManager, 0);
            SaveBeforeLeaving();
            GetSingleton<DialogBoxShower>().Create()
                .SetLocalizedMessage("BeaverBuddies.Client.HostLeft")
                .SetConfirmButton(() => GetSingleton<Timberborn.MainMenuSceneLoading.MainMenuSceneLoader>().OpenMainMenu())
                .Show();
        }

        // The host turned this player away once it was in, for another build or for never saying it: it ends here too, saying why
        private void ShowRefused(ClientEventIO refused)
        {
            Plugin.Log("Turned away by the host: back to the main menu");
            string message = refused.RefusedSilently ? null : $"\n\"{Connect.BuildIdentity.Mismatch(refused.RefusedByHost)}\"";
            _hostSilence?.Close();
            EventIO.Reset();
            SpeedChangePatcher.SetSpeedSilentlyNow(_speedManager, 0);
            var shower = GetSingleton<DialogBoxShower>();
            shower.Create()
                .SetMessage(message == null ? shower._loc.T("BeaverBuddies.JoinCoopGame.Error.Silent")
                    : shower._loc.T("BeaverBuddies.JoinCoopGame.Error.Incompatible") + message)
                .SetConfirmButton(() => GetSingleton<Timberborn.MainMenuSceneLoading.MainMenuSceneLoader>().OpenMainMenu())
                .Show();
        }

        // A connection can stall without closing: the guest hears nothing
        // more and would only stand still, so it's told and can leave
        private void WatchHostSilence()
        {
            if (io is not ClientEventIO client || isReset || _resyncService.IsReloading || GuestLobby.IsOpen)
            {
                _hostSilence?.Close();
                return;
            }
            _hostSilence ??= new HostSilence(GetSingleton<DialogBoxShower>(), LeaveSilentHost);
            _hostSilence.Update(client.SecondsSinceHostHeard);
        }

        private void LeaveSilentHost()
        {
            EventIO.Reset();
            SpeedChangePatcher.SetSpeedSilentlyNow(_speedManager, 0);
            SaveBeforeLeaving();
            GetSingleton<Timberborn.MainMenuSceneLoading.MainMenuSceneLoader>().OpenMainMenu();
        }

        // The save the game makes on leaving for the main menu, which these ways out skip: this player's copy
        // of the game as far as it got, as when they leave it themselves. Made after the game's IO is gone, it
        // still keeps as many autosaves as the settings say, which the autosaver took as the game began (see Autosaves)
        private void SaveBeforeLeaving()
        {
            Plugin.Log("Saving this copy of the game before leaving it");
            try { GetSingleton<Autosaver>()?.CreateExitSave(); }
            catch (Exception e) { Plugin.LogError($"Couldn't save before leaving: {e.Message}"); }
        }

        /**
         * This machine's state differs from the host's: it tells the host, which reloads everyone from its save.
         * Why, when known, goes in the report and in the host's log.
         */
        public void HandleDesync(string why = null)
        {
            if (IsDesynced) return;
            DesyncDetecter.TickLog.Dump("at this machine's desync");
            // The test harness can keep a desynced game as it is, to look at what differs
            if (DevTools.TestHarness.Active && DevTools.TestHarness.NoResync)
            {
                if (!_desyncKept)
                {
                    _desyncKept = true;
                    Plugin.LogError($"[Test] Desync kept, not resyncing:\n{DesyncDetecter.DesyncDetecterService.GetLastDesyncTrace()}");
                    // The host says what differs, and nobody resyncs
                    var kept = DesyncDetecter.DesyncDetecterService.TakeDetail();
                    eventsToSend.Enqueue(new ClientDesyncedEvent()
                    {
                        analyzeOnly = true,
                        playerID = Players.PlayerIdentity.LocalID,
                        desyncTrace = "",
                        detailTick = kept.tick,
                        detailEntityBuckets = kept.entityBuckets,
                        detailWaterAreas = kept.waterAreas,
                        detailEntities = kept.entities,
                        detailWater = kept.water,
                        detailHistory = kept.history,
                        detailTraces = kept.traceLines,
                    });
                    SendEvents();
                }
                return;
            }

            var detail = DesyncDetecterService.TakeDetail();
            // Without tracing, this machine's side of the tick its hashes parted from the host's at
            int splitTick = Settings.Debug ? -1 : DesyncDetecterService.LastSplitTick;
            var snapshot = splitTick >= 0 ? DesyncDetecter.StateDigest.Snapshot(splitTick) : (null, null, null);
            ClientDesyncedEvent e = new ClientDesyncedEvent()
            {
                snapshotTick = splitTick,
                snapshotBuckets = snapshot.buckets,
                snapshotCounts = snapshot.counts,
                snapshotWalkers = snapshot.walkers,
                desyncID = DesyncDetecterService.GetLastDesyncID(),
                desyncTrace = DesyncDetecterService.GetLastDesyncTrace(),
                playerID = Players.PlayerIdentity.LocalID,
                reason = why,
                detailTick = detail.tick,
                detailEntityBuckets = detail.entityBuckets,
                detailWaterAreas = detail.waterAreas,
                detailEntities = detail.entities,
                detailWater = detail.water,
                detailHistory = detail.history,
                detailTraces = detail.traceLines,
            };
            // Nothing more plays here: the host resyncs everyone, sending its
            // save over this same connection
            IsDesynced = true;
            // Don't use EnqueueEventForSending because it shouldn't
            // have a random state set.
            eventsToSend.Enqueue(e);
            e.Replay(this);
            // Send it now: nothing sends from here until the resync
            SendEvents();
            // Pause
            SpeedChangePatcher.SetSpeedSilentlyNow(_speedManager, 0);
        }

        /**
         * Readies and event for sending to connected players.
         * Adds the randomS0 if the event will be played, but assumed
         * this is called *before* the event is played. If this is 
         * called after an event is played, the randomS0 should already
         * be set (it will not be overwritten).
         */
        private void EnqueueEventForSending(ReplayEvent replayEvent)
        {
            // Only set the random state if this recoded event is
            // actually going to be played, or if it's a heartbeat.
            // But don't overwrite the random state if it's already set.
            if (!replayEvent.randomS0Before.HasValue &&
                (EventIO.ShouldPlayPatchedEvents || replayEvent is HeartbeatEvent))
            {
                replayEvent.randomS0Before = UnityEngine.Random.state.s0;
                //Plugin.Log($"Recording event s0: {replayEvent.randomS0Before}");
            }
            eventsToSend.Enqueue(replayEvent);
        }

        private void SendEvents()
        {
            if (EventIO.IsNull) return;
            List<ReplayEvent> events = new List<ReplayEvent>();
            while (eventsToSend.TryDequeue(out ReplayEvent replayEvent))
            {
                replayEvent.ticksSinceLoad = ticksSinceLoad;
                events.Add(replayEvent);
            }
            // Don't send an empty list to save bandwidth.
            if (events.Count == 0) return;
            GroupedEvent group = new GroupedEvent(events);
            group.ticksSinceLoad = ticksSinceLoad;
            EventIO.Get().WriteEvents(group);
        }

        /**
         * Replays any pending events from the user or connected users
         * and then sends successful/pending events to connected users.
         * This should only be called if the game is paused at the end of
         * a tick or right at the start of a tick, so that events always
         * are recorded and replayed at the exact same time in the update loop.
         */
        private void DoTickIO()
        {
            if (_frozenForHosting) return;
            ReplayEvents();
            SendEvents();
        }

        private void Initialize()
        {
            // Start tick at 0
            DesyncDetecterService.StartTick(ticksSinceLoad);

            IsLoaded = true;
        }

        // TODO: Find a better callback way of waiting until initial game
        // loading and randomization is done.
        private int waitUpdates = 2;

        public void UpdateSingleton()
        {
            // Before the host leaving: it closes the connection of whoever it turned away
            if (io is ClientEventIO { RefusedByHost: not null } refused)
            {
                ShowRefused(refused);
                return;
            }
            if (io is ClientEventIO { HostLeft: true })
            {
                ShowHostLeft();
                return;
            }
            WatchHostSilence();
            if (!CanAct)
            {
                // Still listening while desynced, or waiting for a reload,
                // for the host's save
                if (!isReset) io?.Update();
                return;
            }
            if (waitUpdates > 0)
            {
                waitUpdates--;
                return;
            }
            if (waitUpdates == 0)
            {
                Initialize();
                waitUpdates = -1;
            }
            if (io is ServerEventIO server && (server.JoinWaiting || _resyncService.HostPending))
            {
                ReloadForEveryone(server);
                return;
            }
            io.Update();
            // The host's save came in, and another game is loading
            if (!CanAct) return;
            // Only replay events on Update if we're paused by the user.
            // Also only send events if paused, so the client doesn't play
            // then before the end of the tick.
            long part = HitchWatch.Now;
            if (_speedManager.CurrentSpeed == 0 && TargetSpeed == 0)
            {
                DoTickIO();
            }
            else if (io is ClientEventIO)
            {
                // A client's own actions go to the host straight away rather
                // than at its next tick: it doesn't play them itself, and the
                // host gives them the tick they arrive in. Held to the tick,
                // they'd reach the host just after its tick began and wait
                // nearly all of it there
                SendEvents();
            }
            HitchWatch.Part("paused io", part);
            part = HitchWatch.Now;
            UpdateSpeed();
            HitchWatch.Part("speed", part);
        }

        /**
         * Someone joined the game in progress, or a player desynced: at the
         * end of this tick it's saved, and loaded from that save here and
         * by every player.
         */
        private void ReloadForEveryone(ServerEventIO server)
        {
            Plugin.Log("Reloading the game for everyone");
            _resyncService.BeginHostReload();
            FinishFullTickIfNeededAndThen(() =>
            {
                SetTargetSpeed(0);
                SpeedChangePatcher.SetSpeedSilentlyNow(_speedManager, 0);
                // What's been done since the tick began, here or by the
                // others, goes in the save
                io.Update();
                DoTickIO();
                _resyncService.SaveAndReload(server);
            });
        }

        // Set once this game is saved and hosted again: the new server is the saved game's
        private bool _frozenForHosting;

        /**
         * Stops the game for good: it's being saved and hosted again (see
         * ServerHostingUtils). It stays loaded until the host starts the
         * saved game, and mustn't tell the new server its ticks meanwhile:
         * a player joining then would be told the game is that far on.
         */
        public void FreezeForHosting()
        {
            _frozenForHosting = true;
            TargetSpeed = 0;
            SpeedChangePatcher.SetSpeedSilentlyNow(_speedManager, 0);
        }

        // The hosting box was cancelled: the game plays on, as it did before
        public void UnfreezeForHosting()
        {
            _frozenForHosting = false;
        }

        public void SetTargetSpeed(float speed)
        {
            TargetSpeed = speed;
            UpdateSpeed();
        }

        private void UpdateSpeed()
        {
            if (EventIO.IsNull) return;

            // Out of events, a client still plays out the tick it's in: it
            // got that tick's events when it started it, and only needs the
            // next one's to start that (see IsReadyToStartTick). Pausing as
            // soon as its queue empties, at the start of every tick, kept it
            // a whole tick behind the host
            bool midTick = io is ClientEventIO && (_tickingService.NextBucket != 0 || _tickingService.HasTickedReplayService);
            if (io.IsOutOfEvents && !midTick)
            {
                // Also pause the game (silently) if we're out of events
                if (_speedManager.CurrentSpeed != 0)
                {
                    SpeedChangePatcher.SetSpeedSilentlyNow(_speedManager, 0);
                }
                // And return early
                return;
            }

            // If we're not out of ticks to process, check if we're behind
            float targetSpeed = TargetSpeed;
            int ticksBehind = io.TicksBehind;

            // If we're behind, speed up to match, up to 10 (never below the
            // host's own speed, which can be higher than that)
            if (ticksBehind > targetSpeed)
            {
                targetSpeed = Math.Max(targetSpeed, Math.Min(ticksBehind, 10));
            }
            if (io is ClientEventIO) NoteArrivals();
            BucketBoost = io is ClientEventIO && targetSpeed > 0 && ticksBehind <= targetSpeed ? ClientPace() : 1;

            if (_speedManager.CurrentSpeed != targetSpeed)
            {
                //Plugin.Log($"Setting speed to target speed: {targetSpeed}");
                SpeedChangePatcher.SetSpeedSilentlyNow(_speedManager, targetSpeed);
            }
        }

        private float ClientPace() => Math.Max(Slowest, Math.Min(Fastest, 1 + (_margin - ClientCushion) * PaceGain));

        // The host's ticks come in order, each with at least its heartbeat: the ones here that weren't before
        private void NoteArrivals()
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            int tick = Math.Max(_lastTickSeen, ticksSinceLoad);
            while (io.HasEventsForTick(tick + 1))
            {
                tick++;
                _arrivals.Enqueue((tick, now));
            }
            _lastTickSeen = tick;
        }

        // A client at the start of a tick, with the host's tick for it or
        // without: how early it came is the margin the pace keeps
        public void ReachedTickStart(bool ready)
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (!ready)
            {
                if (_waitingSince < 0) _waitingSince = now;
                return;
            }
            float margin = 0;
            if (_waitingSince >= 0)
            {
                margin = _waitingSince - now;
            }
            else if (io is ClientEventIO)
            {
                NoteArrivals();
                int starting = ticksSinceLoad + 1;
                while (_arrivals.Count > 0 && _arrivals.Peek().tick < starting) _arrivals.Dequeue();
                if (_arrivals.Count > 0 && _arrivals.Peek().tick == starting) margin = now - _arrivals.Peek().at;
            }
            _waitingSince = -1;
            _margin += (margin - _margin) * 0.25f;
        }

        // This will be called at the very begining of a tick before
        // anything else has happened, and after everything from the prior
        // tick (including parallel things) has finished.
        public void DoTick()
        {
            if (!CanAct) return;

            if (Settings.Debug && io.ShouldSendHeartbeat)
            {
                // Before incrementing the tick (which creates a new blank trace),
                // capture any unsent traces and send them.
                // Note: this will capture traces for prior ticks, but be sent with
                // an event at the start of the *upcoming* tick.
                foreach (var e in DesyncDetecterService.CreateReplayEventsAndClear())
                {
                    EnqueueEventForSending(e);
                }
            }

            ticksSinceLoad++;

            if (io.ShouldSendHeartbeat)
            {
                // Add a heartbeat if needed to make sure all ticks have
                // at least 1 event, so the clients know we're ticking,
                // and with what state it starts, for them to check theirs
                EnqueueEventForSending(new HeartbeatEvent()
                {
                    hashes = new[] { TEBPatcher.EntityUpdateHash, TEBPatcher.PositionHash, DayClock() },
                });
            }
            // Replay and send events at the change of a tick always.
            // For the server, sending events allows clients to keep playing.
            DoTickIO();

            // Remember DoTickIO can set EventIO to null

            // Log from IO
            io?.Update();

            DesyncDetecterService.RecordHashes(ticksSinceLoad, TEBPatcher.EntityUpdateHash, TEBPatcher.PositionHash, UnityEngine.Random.state.s0, DayClock());

            // IO Complete for Tick: logged every 100th tick, the last 1000 kept for a desync, a resync or a crash
            DesyncDetecter.TickLog.Record(ticksSinceLoad, (uint)TEBPatcher.EntityUpdateHash, (uint)TEBPatcher.PositionHash, (uint)UnityEngine.Random.state.s0);

            // A guest's state checked against the host's every tick; with tracing on, its traces do more
            if (io is ClientEventIO && !Settings.Debug && DesyncDetecterService.DiffersFromHost(ticksSinceLoad))
            {
                HandleDesync(DesyncDetecterService.LastSplit);
            }

            if (ticksSinceLoad % 20 == 0)
            {
                //gameSaveHelper.LogStateCheck(ticksSinceLoad);
            }

            // Update speed and pause if needed for the new tick.
            UpdateSpeed();

            if (io is ServerEventIO server && ticksSinceLoad == 1)
            {
                // A game that ticks is in, should PostLoad not have said so
                if (server.Stage == ServerEventIO.HostingStage.Loading) server.Stage = ServerEventIO.HostingStage.Running;
                server.OnGameStarted();
            }
        }

        public void FinishFullTickIfNeededAndThen(Action action)
        {
            // If we're paused, we should be at the end of a tick anyway
            if (_speedManager.CurrentSpeed == 0)
            {
                action();
                return;
            }
            // If we're not paused, we need to wait until the end of the tick
            _tickingService.FinishFullTickAndThen(action);
        }

        /**
         * Does something with the game paused, then plays on at the speed it had: the host's own saves
         * (see GameSaverSavePatcher), at the end of a tick. The players behind the host wait at that
         * tick while it's paused, as they would while it saves, so it's told to nobody.
         */
        public void WhilePaused(Action action)
        {
            float speed = _speedManager.CurrentSpeed, target = TargetSpeed;
            if (speed == 0)
            {
                action();
                return;
            }
            Plugin.Log("Pausing for the host's save");
            SetTargetSpeed(0);
            SpeedChangePatcher.SetSpeedSilentlyNow(_speedManager, 0);
            try
            {
                action();
            }
            finally
            {
                SetTargetSpeed(target);
                SpeedChangePatcher.SetSpeedSilentlyNow(_speedManager, speed);
                Plugin.Log($"Playing on at speed {speed} after the host's save");
            }
        }
    }

    [HarmonyPatch(typeof(TickableSingletonService), nameof(TickableSingletonService.Load))]
    static class TickableSingletonServicePatcher
    {
        static void Postfix(TickableSingletonService __instance)
        {
            // Ensure late singletons come first
            // Create a new list, since the variable is immutable
            var tickableSingletons = new List<MeteredSingleton>(__instance._tickableSingletons);
            var earlySingletons = tickableSingletons
                .Where(s => s._tickableSingleton is IEarlyTickableSingleton).ToList();
            foreach ( var earlySingleton in earlySingletons)
            {
                tickableSingletons.Remove(earlySingleton);
            }
            tickableSingletons.InsertRange(0, earlySingletons);
            __instance._tickableSingletons = tickableSingletons.ToImmutableArray();
        }
    }

    public class TickingService : RegisteredSingleton
    {
        /// <summary>
        /// If true, the TickingService will stop as soon as possible (interrupting
        /// a normal update, but finishing it's current bucket), but it will then resume
        /// on the following update.
        /// </summary>
        public bool ShouldInterruptTicking { get; set; } = false;
        public bool ShouldCompleteFullTick { get; private set; } = false;

        public bool HasTickedReplayService { get; private set; } = false;

        public int NextBucket { get; private set; } = 0;

        public ReplayService replayService { get; set; }

        // Should be ok non-concurrent - for now only main thread call this
        private List<Action> onCompletedFullTick = new List<Action>();

        public void FinishFullTick()
        {
            ShouldCompleteFullTick = true;
        }

        public void FinishFullTickAndThen(Action value)
        {
            onCompletedFullTick.Add(value);
            ShouldCompleteFullTick = true;
        }

        internal void OnTickingCompleted()
        {
            // Interruptions are always temporary and get reset at the end of
            // each ticking update
            ShouldInterruptTicking = false;
            if (!ShouldCompleteFullTick) return;
            Plugin.Log($"Finished full tick; calling {onCompletedFullTick.Count} callbacks");
            foreach (var action in onCompletedFullTick)
            {
                action();
            }
            onCompletedFullTick.Clear();
            ShouldCompleteFullTick = false;
        }

        // Since when a guest has waited for the host's next tick, and which; -1 when it isn't waiting
        private float _waitingSince = -1;
        private int _waitingFor;
        // Waits shorter than this happen all the time and aren't said
        private const float LongWait = 1;

        // A wait for the host's tick that's over, said if it was a long one: one that doesn't end is the
        // host going quiet (see HostSilence)
        private void EndWait()
        {
            if (_waitingSince < 0) return;
            float waited = UnityEngine.Time.realtimeSinceStartup - _waitingSince;
            _waitingSince = -1;
            if (waited >= LongWait) Plugin.Log($"Waited {waited.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} s for the host's tick {_waitingFor}");
        }

        private bool ShouldTick(TickableBucketService __instance, int numberOfBucketsToTick)
        {

            // Don't tick if we've been interrupted by a forced pause
            if (ShouldInterruptTicking) return false;

            // Don't tick if we've set the game speed to 0 (paused)
            if (replayService.TargetSpeed == 0)
            {
                // A pause isn't waiting for the host
                _waitingSince = -1;
                return false;
            }

            if (IsAtStartOfTick(__instance) && !HasTickedReplayService)
            {
                // Don't start a brand new tick until the ReplayService
                // is ready. Note that this is only a reason to stop if
                // we're at the *very start* of a new tick - otherwise
                // it would be overly conservative.
                bool ready = replayService.IsReadyToStartTick;
                if (replayService.TargetSpeed > 0) replayService.ReachedTickStart(ready);
                if (!ready)
                {
                    HitchWatch.Stalled();
                    // A guest waiting for the host's next tick, as one keeping close behind it does now and then
                    if (replayService.TicksSinceLoad > 0 && _waitingSince < 0)
                    {
                        _waitingSince = UnityEngine.Time.realtimeSinceStartup;
                        _waitingFor = replayService.TicksSinceLoad + 1;
                    }
                    return false;
                }
                EndWait();
            }

            // If we need to complete a full tick, make sure to go exactly until
            // the end of this tick, to ensure an update follows immediately.
            if (ShouldCompleteFullTick)
            {
                return __instance._nextBucketIndex != 0;
            }
            return numberOfBucketsToTick > 0;
        }

        public static bool IsAtStartOfTick(TickableBucketService __instance)
        {
            // For Update 7, index is 0 for singleton ticking
            return __instance._nextBucketIndex == 0;
        }

        private bool TickReplayServiceOrNextBucket(TickableBucketService __instance)
        {
            if (IsAtStartOfTick(__instance))
            {
                // If we're at the start of a tick, and we haven't yet
                // ticked the ReplayService...
                if (!HasTickedReplayService)
                {
                    // First finish any parallel ticks
                    using (ReplayEvent.InGameTick())
                        ((TickableSingletonService)__instance._tickableSingletonService).FinishParallelTick();

                    // Tick it and stop
                    HasTickedReplayService = true;
                    long started = HitchWatch.Now;
                    replayService?.DoTick();
                    HitchWatch.TickedIO(started);
                    return true;
                }
                // Otherwise if we're still at the beginning
                // reset the flag
                HasTickedReplayService = false;
            }
            int bucket = __instance._nextBucketIndex;
            long start = HitchWatch.Now;
            // The game's own tick: what it calls is game logic, never a player's action (see ReplayEvent.InGameTick)
            using (ReplayEvent.InGameTick()) __instance.TickNextBucket();
            HitchWatch.TickedBucket(bucket, start);
            NextBucket = __instance._nextBucketIndex;
            return false;
        }

        private float _extraBuckets;

        public bool TickBuckets(TickableBucketService __instance, int numberOfBucketsToTick)
        {

            // TODO: I think if number of buckets starts at 0, we should unmark
            // complete full tick and return because it means we're paused...
            // Alternatively, I think that we could use the ReplayService version
            // that only stops ticking if not paused

#if ONE_TICK_PER_UPDATE
            // Forces 1 tick per update
            if (numberOfBucketsToTick != 0)
            {
                // Don't need to add one anymore; singletons are included
                numberOfBucketsToTick = __instance.NumberOfBuckets;
            }
#endif

            // A client keeping pace with the host plays a few more or fewer
            // buckets a frame (see ReplayService.BucketBoost): the game's own
            // speed doesn't change how fast it ticks
            if (numberOfBucketsToTick > 0 && replayService != null && replayService.BucketBoost != 1)
            {
                _extraBuckets += numberOfBucketsToTick * replayService.BucketBoost;
                numberOfBucketsToTick = (int)_extraBuckets;
                _extraBuckets -= numberOfBucketsToTick;
            }

            long ticking = HitchWatch.Now;
            while (ShouldTick(__instance, numberOfBucketsToTick--))
            {
                if (TickReplayServiceOrNextBucket(__instance))
                {
                    // Refund a bucket if we ticked the ReplayService
                    numberOfBucketsToTick++;
                }
            }

            HitchWatch.Ticked(ticking);

            // Tell the TickRequester we've finished this partial (or possibly complete) tick
            OnTickingCompleted();

            // Replace the default behavior entirely
            return false;
        }
    }

    [ManualMethodOverwrite]
    /*
        4/19/2025
        // Also check TickNextBucket - we rework everything
		while (numberOfBucketsToTick-- > 0)
		{
			TickNextBucket();
		}
     */
    [HarmonyPatch(typeof(TickableBucketService), nameof(TickableBucketService.TickBuckets))]
    static class TickableBucketServiceTickUpdatePatcher
    {
        static bool Prefix(TickableBucketService __instance, int numberOfBucketsToTick)
        {
            if (EventIO.IsNull) return true;
            TickingService ts = GetSingleton<TickingService>();
            if (ts == null) return true;
            return ts.TickBuckets(__instance, numberOfBucketsToTick);
        }
    }
}

using Bindito.Core;
using HarmonyLib;
using System;

namespace BeaverBuddies.DevTools
{
    /**
     * The test harness's start. Its DLL, BeaverBuddies.TestHarness, is only ever installed by a test run, beside
     * BeaverBuddies' own in the mod's folder; the game loads every DLL there before any mod starts, and Plugin.StartMod
     * starts this once BeaverBuddies' own patches are in. Without BB_INSTANCE (the player's own game playing from a
     * Mods folder a run installed into, BB_HOME=$HOME) it does nothing. In a copy the runner started, it reads the
     * switches the runner hands over (lib.sh's bb_open), sets what BeaverBuddies reads of them (HarnessFlags) and
     * applies the harness's patches: under BeaverBuddies' own Harmony ID, so the load shortcuts' checks for other mods'
     * patches see them as the mod's own, as they were when they were in its DLL.
     */
    public class HarnessStarter : IHarnessStarter
    {
        public void Start()
        {
            if (!TestHarness.Active)
            {
                Plugin.Log("The test harness is installed, but this isn't a test copy (no BB_INSTANCE): it stays off");
                return;
            }
            HarnessFlags.Active = true;
            HarnessEvent.Run = TestHarness.RunReplayed;
            // Every tick's line, which the scenarios and their helpers read, unless told to log as a player's game does
            HarnessFlags.EveryTickLogged = Environment.GetEnvironmentVariable("BB_TICKLOG_LIVE") != "1";
            HarnessFlags.FakeBuild = Told("BB_FAKE_BUILD");
            HarnessFlags.JoinCode = Told("BB_RTC_CODE");
            if (double.TryParse(Environment.GetEnvironmentVariable("BB_HITCH_MS"), out double hitchMs)) HarnessFlags.HitchMs = hitchMs;
            if (double.TryParse(Environment.GetEnvironmentVariable("BB_PACE_SECONDS"), out double paceSeconds)) HarnessFlags.PaceSeconds = paceSeconds;
            // The host's connections over TCP, as the guest's are (see Switches.ClientStream)
            TimberNet.TCPListenerWrapper.Wrap = stream => LossyCloseStream.Wrap(SilentFirstStream.Wrap(stream));
            var harmony = new Harmony(Plugin.ID);
            foreach (Type type in AccessTools.GetTypesFromAssembly(typeof(HarnessStarter).Assembly))
            {
                if (type.HasHarmonyAttribute()) harmony.CreateClassProcessor(type).Patch();
            }
            Plugin.Log($"The test harness is on: this copy is {TestHarness.Instance}");
        }

        private static string Told(string name) => Environment.GetEnvironmentVariable(name) is string value && value != "" ? value : null;
    }

    // The harness's singleton, at the main menu and in every game, in the copies it drives. The game finds
    // configurators in every DLL it has loaded
    [Context("MainMenu")]
    [Context("Game")]
    public class TestHarnessConfigurator : IConfigurator
    {
        public void Configure(IContainerDefinition containerDefinition)
        {
            if (HarnessFlags.Active) containerDefinition.Bind<TestHarness>().AsSingleton();
        }
    }
}

using ModSettings.Common;
using ModSettings.Core;
using System.Linq;
using Timberborn.Modding;
using Timberborn.SettingsSystem;

namespace BeaverBuddies
{
    public enum PauseReductionLevel
    {
        Off = 0,
        MenuOnly = 1,
        NeverAutoPause = 2,
    }

    public class Settings : ModSettingsOwner
    {
        public ModSetting<string> ClientConnectionAddress { get; } =
            new("127.0.0.1",
                ModSettingDescriptor.CreateLocalized(
                    "BeaverBuddies.Settings.ClientConnectionAddress"
                ).SetLocalizedTooltip("BeaverBuddies.Settings.ClientConnectionAddress.Tooltip")
            );

        public ModSetting<int> DefaultPort { get; } =
            new(25565,
                ModSettingDescriptor.CreateLocalized(
                    "BeaverBuddies.Settings.Port"
                ).SetLocalizedTooltip("BeaverBuddies.Settings.Port.Tooltip")
            );

        public ModSetting<bool> ShowFirstTimerMessage { get; } =
            new(true,
                ModSettingDescriptor.CreateLocalized(
                    "BeaverBuddies.Settings.ShowFirstTimerMessage"
                )
        );

        // ---- Steam Settings ----

        public ModSetting<bool> EnableSteamConnection { get; } =
            new(true,
            ModSettingDescriptor.CreateLocalized(
                "BeaverBuddies.Settings.EnableSteamConnection"
            ).SetLocalizedTooltip("BeaverBuddies.Settings.EnableSteamConnection.Tooltip")
        );

        public ModSetting<bool> FriendsCanJoinSteamGame { get; } =
            new(true,
            ModSettingDescriptor.CreateLocalized(
                "BeaverBuddies.Settings.FriendsCanJoinSteamGame"
            ).SetLocalizedTooltip("BeaverBuddies.Settings.FriendsCanJoinSteamGame.Tooltip")
        );

        // On, a player on another build (see Connect.BuildIdentity) can't join this player's game, nor
        // this player theirs, however they join; off, they're warned and play on, maybe out of sync
        public ModSetting<bool> StrictBuildMatch { get; } =
            new(true,
            ModSettingDescriptor.CreateLocalized(
                "BeaverBuddies.Settings.StrictBuildMatch"
            ).SetLocalizedTooltip("BeaverBuddies.Settings.StrictBuildMatch.Tooltip")
        );

        // On, the game runs on behind other apps while hosted or joined, whatever the game's own
        // "Run in background" says (see Connect.BackgroundPlay); off, the game's own setting applies
        public ModSetting<bool> KeepPlayingInBackground { get; } =
            new(true,
            ModSettingDescriptor.CreateLocalized(
                "BeaverBuddies.Settings.KeepPlayingInBackground"
            ).SetLocalizedTooltip("BeaverBuddies.Settings.KeepPlayingInBackground.Tooltip")
        );

        // ---- Quality of Life Settings ----

        // What pauses everyone in a game: the host's choice, for every player in it (see PauseReductionSetting).
        // Nothing by default: each player's menu is their own, and pausing everyone whenever someone opens
        // the menu, a building's deletion or a research dialog interrupts too much
        public LimitedStringModSetting PauseReduction { get; } =
            new(2, new[] {
                new LimitedStringModSettingValue("0", "BeaverBuddies.Settings.PauseReduction.Off"),
                new LimitedStringModSettingValue("1", "BeaverBuddies.Settings.PauseReduction.LowRisk"),
                new LimitedStringModSettingValue("2", "BeaverBuddies.Settings.PauseReduction.HighRisk")
            }, ModSettingDescriptor.CreateLocalized("BeaverBuddies.Settings.PauseReduction")
                .SetLocalizedTooltip("BeaverBuddies.Settings.PauseReduction.Tooltip")
        );

        // A save the host makes itself is written with the game paused, as a save made alone is (see GameSaverSavePatcher)
        public ModSetting<bool> PauseForHostSaves { get; } =
            new(true,
            ModSettingDescriptor.CreateLocalized(
                "BeaverBuddies.Settings.PauseForHostSaves"
            ).SetLocalizedTooltip("BeaverBuddies.Settings.PauseForHostSaves.Tooltip")
        );

        // ---- Developer Settings ----

        public ModSetting<bool> AlwaysTrace { get; } =
            new(false,
            ModSettingDescriptor.CreateLocalized(
                "BeaverBuddies.Settings.AlwaysTrace"
            ).SetLocalizedTooltip("BeaverBuddies.Settings.AlwaysTrace.Tooltip")
        );

        public ModSetting<bool> SilenceLogging { get; } =
            new(false,
                ModSettingDescriptor.CreateLocalized(
                    "BeaverBuddies.Settings.SilenceLogging"
                ).SetLocalizedTooltip("BeaverBuddies.Settings.SilenceLogging.Tooltip")
        );

        // ---- Ping Settings ----

        public const string DefaultPingPlayerName = "Player";
        public ModSetting<string> PingPlayerName { get; } =
            new(DefaultPingPlayerName,
                ModSettingDescriptor.CreateLocalized(
                    "BeaverBuddies.Settings.PingPlayerName"
                ).SetLocalizedTooltip("BeaverBuddies.Settings.PingPlayerName.Tooltip")
        );

        public LimitedStringModSetting PreferredColor { get; } =
            new(0, Players.PlayerColors.Palette.Select((c, i) =>
                    new LimitedStringModSettingValue(i.ToString(), "BeaverBuddies.Settings.PreferredColor." + c.Key)).ToArray(),
                ModSettingDescriptor.CreateLocalized("BeaverBuddies.Settings.PreferredColor")
                    .SetLocalizedTooltip("BeaverBuddies.Settings.PreferredColor.Tooltip")
        );

        public ModSetting<bool> ShowPlayerCursors { get; } =
            new(true,
                ModSettingDescriptor.CreateLocalized(
                    "BeaverBuddies.Settings.ShowPlayerCursors"
                ).SetLocalizedTooltip("BeaverBuddies.Settings.ShowPlayerCursors.Tooltip")
        );

        // We keep a static instance because
        // 1) The settings are saved in a static manner, so all instances
        //    should be identical, and
        // 2) We need to access the settings frequently without an easy
        //    way to pass the instance around.
        private static Settings instance = null;

        /**
         * Indicates that the player has temporarily enabled debug mode.
         * Not serialized as a setting.
         */
        public static bool TemporarilyDebug { get; set; } = false;

        public static bool Debug => TemporarilyDebug || (instance?.AlwaysTrace.Value ?? false);

        public static bool VerboseLogging => !(instance?.SilenceLogging.Value == true);
        public static int Port => instance?.DefaultPort.Value ?? 25565;
        public static bool EnableSteam => instance?.EnableSteamConnection.Value ?? true;
        public static bool LobbyJoinable => instance?.FriendsCanJoinSteamGame.Value ?? true;
        // Two test copies share these settings: each can be told otherwise (BB_STRICT, see DevTools.TestHarness)
        public static bool StrictBuild =>
            DevTools.TestHarness.Active && System.Environment.GetEnvironmentVariable("BB_STRICT") is string strict && strict != ""
                ? strict != "0"
                : instance?.StrictBuildMatch.Value ?? true;
        public static bool PlayInBackground => instance?.KeepPlayingInBackground.Value ?? true;
        public static bool ShouldShowFirstTimerMessage => instance?.ShowFirstTimerMessage.Value ?? true;
        // Saved with each game, picked when it's created (see DevTools.DevToolsRule)
        public static bool AllowDevTools => DevTools.DevToolsRule.Allowed;

        public static bool PauseHostSaves => instance?.PauseForHostSaves.Value ?? true;

        // What pauses everyone: in another's game, the host's choice (see IO.ClientEventIO.HostPauses), so one
        // player's own can't pause the rest; this player's own when it hosts or plays alone
        public static PauseReductionLevel PauseReductionSetting =>
            IO.EventIO.Get() is IO.ClientEventIO { HostPauses: int host } ? (PauseReductionLevel)host : OwnPauseReduction;

        // This player's own choice. Test copies share these settings: each can be told otherwise (BB_PAUSES)
        public static PauseReductionLevel OwnPauseReduction
        {
            get
            {
                if (DevTools.TestHarness.Active && int.TryParse(System.Environment.GetEnvironmentVariable("BB_PAUSES"), out int told))
                    return (PauseReductionLevel)told;

                if (instance?.PauseReduction?.Value == null)
                    return PauseReductionLevel.Off;

                if (int.TryParse(instance.PauseReduction.Value, out int level))
                    return (PauseReductionLevel)level;

                return PauseReductionLevel.Off;
            }
        }

        // From the host's box, where players set who they are
        public static void SetPlayer(string name, int colorIndex)
        {
            if (instance == null) return;
            if (!string.IsNullOrWhiteSpace(name)) instance.PingPlayerName.SetValue(name.Trim());
            instance.PreferredColor.SetValue(Players.PlayerColors.Clamp(colorIndex).ToString());
        }

        public static string PingDisplayName => instance?.PingPlayerName.Value ?? DefaultPingPlayerName;

        // Two test copies share these settings: the second prefers red (see DevTools.TestHarness)
        public static int PreferredColorIndex =>
            DevTools.TestHarness.Active && DevTools.TestHarness.Instance != "host" ? 1
            : int.TryParse(instance?.PreferredColor?.Value, out int index) ? index : 0;

        public static UnityEngine.Color PingColorValue =>
            Players.PlayerColors.Palette[Players.PlayerPresence.LocalColorIndex()].Color;

        public static bool ShowCursors => instance?.ShowPlayerCursors.Value ?? true;

        public Settings(ISettings settings,
                        ModSettingsOwnerRegistry modSettingsOwnerRegistry,
                        ModRepository modRepository) :
            base(settings, modSettingsOwnerRegistry, modRepository)
        {
            instance = this;
        }

        // "Always trace" as last loaded, for what reads it as a scene loads, a WebRTC connection set up then, say: each
        // scene's own settings hold only the defaults until they're loaded. Debug reads those then, and the desync
        // traces count on that: traced from the load, a host's new game and its guest's load from its save differ
        public static bool AlwaysTraceLoaded { get; private set; }

        protected override void OnAfterLoad()
        {
            base.OnAfterLoad();
            AlwaysTraceLoaded = AlwaysTrace.Value;
        }

        protected override string ModId => Plugin.ID;

    }
}

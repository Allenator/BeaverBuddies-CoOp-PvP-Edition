using ModSettings.Core;
using System.Globalization;

namespace BeaverBuddies
{
    public enum PauseReductionLevel
    {
        Off = 0,
        MenuOnly = 1,
        NeverAutoPause = 2,
    }

    /**
     * BeaverBuddies' settings, read from wherever they're needed. Each is kept in a section of the mod's settings
     * screen (see SettingsSection), and read here from the section's copy that last loaded: each scene makes its own,
     * which holds only the defaults until its values are loaded, so what's read as a scene loads (the desync traces,
     * a WebRTC connection being set up) still gets the player's choices.
     */
    public class Settings
    {
        // Each section once its values are loaded (see SettingsSection)
        internal static YouSettings You;
        internal static MultiplayerSettings Multiplayer;
        internal static HostingSettings Hosting;
        internal static AutosaveSettings Autosaves;
        internal static DeveloperSettings Developer;

        // This scene's own, for what sets a setting
        private readonly YouSettings _you;
        private readonly MultiplayerSettings _multiplayer;

        public Settings(YouSettings you, MultiplayerSettings multiplayer)
        {
            _you = you;
            _multiplayer = multiplayer;
        }

        public ModSetting<string> PingPlayerName => _you.PingPlayerName;
        public ModSetting<bool> ShowFirstTimerMessage => _you.ShowFirstTimerMessage;
        public ModSetting<string> ClientConnectionAddress => _multiplayer.ClientConnectionAddress;
        public ModSetting<int> DefaultPort => _multiplayer.DefaultPort;

        /**
         * Indicates that the player has temporarily enabled debug mode.
         * Not serialized as a setting.
         */
        public static bool TemporarilyDebug { get; set; } = false;

        public static bool Debug => TemporarilyDebug || (Developer?.AlwaysTrace.Value ?? false);

        public static int Port => Multiplayer?.DefaultPort.Value ?? 25565;
        public static bool EnableSteam => Multiplayer?.EnableSteamConnection.Value ?? true;
        public static bool LobbyJoinable => Hosting?.FriendsCanJoinSteamGame.Value ?? true;
        // Two test copies share these settings: each can be told otherwise (BB_STRICT, see DevTools.TestHarness)
        public static bool StrictBuild =>
            DevTools.TestHarness.Active && System.Environment.GetEnvironmentVariable("BB_STRICT") is string strict && strict != ""
                ? strict != "0"
                : Multiplayer?.StrictBuildMatch.Value ?? true;
        public static bool PlayInBackground => Multiplayer?.KeepPlayingInBackground.Value ?? true;

        // A game hosted or joined, or of the Multiplayer settlement: autosaved every so many minutes, keeping so many, and
        // not while paused (see Connect.Autosaves)
        public static float AutosaveMinutes =>
            float.TryParse(Autosaves?.AutosaveMinutes?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float minutes) && minutes > 0 ? minutes : 10;
        public static int AutosavesKept =>
            int.TryParse(Autosaves?.AutosavesKept?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int kept) && kept > 0 ? kept : 12;
        public static bool SkipPausedAutosaves => Autosaves?.SkipPausedAutosaves.Value ?? true;
        public static bool ShouldShowFirstTimerMessage => You?.ShowFirstTimerMessage.Value ?? true;
        // Saved with each game, picked when it's created (see DevTools.DevToolsRule)
        public static bool AllowDevTools => DevTools.DevToolsRule.Allowed;

        public static bool PauseHostSaves => Hosting?.PauseForHostSaves.Value ?? true;

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

                if (Hosting?.PauseReduction?.Value == null)
                    return PauseReductionLevel.Off;

                if (int.TryParse(Hosting.PauseReduction.Value, out int level))
                    return (PauseReductionLevel)level;

                return PauseReductionLevel.Off;
            }
        }

        // From the host's box, where players set who they are
        public static void SetPlayer(string name, int colorIndex)
        {
            if (You == null) return;
            if (!string.IsNullOrWhiteSpace(name)) You.PingPlayerName.SetValue(name.Trim());
            You.PreferredColor.SetValue(Players.PlayerColors.Clamp(colorIndex).ToString());
        }

        public const string DefaultPingPlayerName = "Player";
        public static string PingDisplayName => You?.PingPlayerName.Value ?? DefaultPingPlayerName;

        // Two test copies share these settings: the second prefers red (see DevTools.TestHarness)
        public static int PreferredColorIndex =>
            DevTools.TestHarness.Active && DevTools.TestHarness.Instance != "host" ? 1
            : int.TryParse(You?.PreferredColor?.Value, out int index) ? index : 0;

        public static UnityEngine.Color PingColorValue =>
            Players.PlayerColors.Palette[Players.PlayerPresence.LocalColorIndex()].Color;

        public static bool ShowCursors => Multiplayer?.ShowPlayerCursors.Value ?? true;
    }
}

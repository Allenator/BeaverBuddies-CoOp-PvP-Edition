using ModSettings.Common;
using ModSettings.Core;
using System.Linq;
using Timberborn.Modding;
using Timberborn.SettingsSystem;
using UnityEngine;

namespace BeaverBuddies
{
    /**
     * A section of BeaverBuddies' settings screen, under its own heading, in its place among the others (Order).
     * Each setting is added by the key it has always been saved under, "Settings." and its name, so what a player
     * chose stays theirs whichever section it's in: they're fields, not properties, as the ModSettings mod adds a
     * section's properties itself, under keys of the section's name. Read through Settings.
     */
    public abstract class SettingsSection : ModSettingsOwner
    {
        protected SettingsSection(ISettings settings, ModSettingsOwnerRegistry modSettingsOwnerRegistry, ModRepository modRepository) :
            base(settings, modSettingsOwnerRegistry, modRepository)
        {
        }

        protected override string ModId => Plugin.ID;

        // A setting's label and tooltip, by its name
        protected static ModSettingDescriptor Described(string name) =>
            ModSettingDescriptor.CreateLocalized("BeaverBuddies.Settings." + name).SetLocalizedTooltip("BeaverBuddies.Settings." + name + ".Tooltip");

        protected void Add<T>(ModSetting<T> setting, string name) => AddCustomModSetting(setting, "Settings." + name);

        // This section's settings, in the order they're shown
        protected abstract void AddSettings();

        protected override void OnBeforeLoad()
        {
            base.OnBeforeLoad();
            AddSettings();
        }
    }

    /** Who the player is to the others. */
    public class YouSettings : SettingsSection
    {
        public readonly ModSetting<string> PingPlayerName = new(Settings.DefaultPingPlayerName, Described(nameof(PingPlayerName)));

        public readonly LimitedStringModSetting PreferredColor =
            new(0, Players.PlayerColors.Palette.Select((c, i) =>
                    new LimitedStringModSettingValue(i.ToString(), "BeaverBuddies.Settings.PreferredColor." + c.Key)).ToArray(),
                Described(nameof(PreferredColor)));

        public readonly ModSetting<bool> ShowFirstTimerMessage = new(true, Described(nameof(ShowFirstTimerMessage)));

        public YouSettings(ISettings settings, ModSettingsOwnerRegistry modSettingsOwnerRegistry, ModRepository modRepository) :
            base(settings, modSettingsOwnerRegistry, modRepository)
        {
        }

        public override int Order => 0;
        public override string HeaderLocKey => "BeaverBuddies.Settings.Section.You";

        protected override void AddSettings()
        {
            Add(PingPlayerName, nameof(PingPlayerName));
            Add(PreferredColor, nameof(PreferredColor));
            Add(ShowFirstTimerMessage, nameof(ShowFirstTimerMessage));
        }

        protected override void OnAfterLoad()
        {
            base.OnAfterLoad();
            Settings.You = this;
        }
    }

    /** What applies both hosting and joining. */
    public class MultiplayerSettings : SettingsSection
    {
        // Each player's own: on, they see the others' cursors and share theirs; off, neither (see Cursors.PlayerCursorService)
        public readonly ModSetting<bool> ShowPlayerCursors = new(true, Described(nameof(ShowPlayerCursors)));

        // On, the game runs on behind other apps while hosted or joined, whatever the game's own
        // "Run in background" says (see Connect.BackgroundPlay); off, the game's own setting applies
        public readonly ModSetting<bool> KeepPlayingInBackground = new(true, Described(nameof(KeepPlayingInBackground)));

        // This machine's autosaves of a game hosted or joined (see Connect.Autosaves): how often, how many are kept, and
        // none while it's paused; a game alone autosaves as the game does, every 10 minutes keeping 3
        public static readonly string[] AutosaveMinuteValues = { "5", "10", "15", "20", "30" };
        public static readonly string[] AutosavesKeptValues = { "3", "6", "12", "24", "48" };

        public readonly LimitedStringModSetting AutosaveMinutes =
            new(1, AutosaveMinuteValues.Select(m => new LimitedStringModSettingValue(m, "BeaverBuddies.Settings.AutosaveMinutes." + m)).ToArray(),
                Described(nameof(AutosaveMinutes)));

        public readonly LimitedStringModSetting AutosavesKept =
            new(2, AutosavesKeptValues.Select(n => new LimitedStringModSettingValue(n, "BeaverBuddies.Settings.AutosavesKept." + n)).ToArray(),
                Described(nameof(AutosavesKept)));

        public readonly ModSetting<bool> SkipPausedAutosaves = new(true, Described(nameof(SkipPausedAutosaves)));

        // On, a player on another build (see Connect.BuildIdentity) can't join this player's game, nor
        // this player theirs, however they join; off, they're warned and play on, maybe out of sync
        public readonly ModSetting<bool> StrictBuildMatch = new(true, Described(nameof(StrictBuildMatch)));

        public readonly ModSetting<bool> EnableSteamConnection = new(true, Described(nameof(EnableSteamConnection)));

        public readonly ModSetting<string> ClientConnectionAddress = new("127.0.0.1", Described(nameof(ClientConnectionAddress)));

        // Labeled by "Port"'s texts, and saved as DefaultPort, as it always was
        public readonly ModSetting<int> DefaultPort = new(25565, Described("Port"));

        public MultiplayerSettings(ISettings settings, ModSettingsOwnerRegistry modSettingsOwnerRegistry, ModRepository modRepository) :
            base(settings, modSettingsOwnerRegistry, modRepository)
        {
        }

        public override int Order => 1;
        public override string HeaderLocKey => "BeaverBuddies.Settings.Section.Multiplayer";

        protected override void AddSettings()
        {
            Add(ShowPlayerCursors, nameof(ShowPlayerCursors));
            Add(KeepPlayingInBackground, nameof(KeepPlayingInBackground));
            Add(AutosaveMinutes, nameof(AutosaveMinutes));
            Add(AutosavesKept, nameof(AutosavesKept));
            Add(SkipPausedAutosaves, nameof(SkipPausedAutosaves));
            Add(StrictBuildMatch, nameof(StrictBuildMatch));
            Add(EnableSteamConnection, nameof(EnableSteamConnection));
            Add(ClientConnectionAddress, nameof(ClientConnectionAddress));
            Add(DefaultPort, nameof(DefaultPort));
        }

        protected override void OnAfterLoad()
        {
            base.OnAfterLoad();
            Settings.Multiplayer = this;
        }
    }

    /** What a host decides for its own games, under a line saying so. */
    public class HostingSettings : SettingsSection
    {
        public readonly ReadonlyTextModSetting Note =
            new(ModSettingDescriptor.CreateLocalized("BeaverBuddies.Settings.Section.Hosting.Note"),
                new ReadonlyTextModSetting.TextSettings(TextAnchor.MiddleLeft, 14, new Vector4(0, 0, 0, 6)));

        // What pauses everyone in a game: the host's choice, for every player in it (see Settings.PauseReductionSetting).
        // Nothing by default: each player's menu is their own, and pausing everyone whenever someone opens
        // the menu, a building's deletion or a research dialog interrupts too much
        public readonly LimitedStringModSetting PauseReduction =
            new(2, new[] {
                new LimitedStringModSettingValue("0", "BeaverBuddies.Settings.PauseReduction.Off"),
                new LimitedStringModSettingValue("1", "BeaverBuddies.Settings.PauseReduction.LowRisk"),
                new LimitedStringModSettingValue("2", "BeaverBuddies.Settings.PauseReduction.HighRisk")
            }, Described(nameof(PauseReduction)));

        // A save the host makes itself is written with the game paused, as a save made alone is (see GameSaverSavePatcher)
        public readonly ModSetting<bool> PauseForHostSaves = new(true, Described(nameof(PauseForHostSaves)));

        public readonly ModSetting<bool> FriendsCanJoinSteamGame = new(true, Described(nameof(FriendsCanJoinSteamGame)));

        public HostingSettings(ISettings settings, ModSettingsOwnerRegistry modSettingsOwnerRegistry, ModRepository modRepository) :
            base(settings, modSettingsOwnerRegistry, modRepository)
        {
        }

        public override int Order => 2;
        public override string HeaderLocKey => "BeaverBuddies.Settings.Section.Hosting";

        protected override void AddSettings()
        {
            AddNonPersistentModSetting(Note);
            Add(PauseReduction, nameof(PauseReduction));
            Add(PauseForHostSaves, nameof(PauseForHostSaves));
            Add(FriendsCanJoinSteamGame, nameof(FriendsCanJoinSteamGame));
        }

        protected override void OnAfterLoad()
        {
            base.OnAfterLoad();
            Settings.Hosting = this;
        }
    }

    /** Desync tracing, for reports and developers. */
    public class DeveloperSettings : SettingsSection
    {
        public readonly ModSetting<bool> AlwaysTrace = new(false, Described(nameof(AlwaysTrace)));

        public DeveloperSettings(ISettings settings, ModSettingsOwnerRegistry modSettingsOwnerRegistry, ModRepository modRepository) :
            base(settings, modSettingsOwnerRegistry, modRepository)
        {
        }

        public override int Order => 3;
        public override string HeaderLocKey => "BeaverBuddies.Settings.Section.Developer";

        protected override void AddSettings()
        {
            Add(AlwaysTrace, nameof(AlwaysTrace));
        }

        protected override void OnAfterLoad()
        {
            base.OnAfterLoad();
            Settings.Developer = this;
        }
    }
}

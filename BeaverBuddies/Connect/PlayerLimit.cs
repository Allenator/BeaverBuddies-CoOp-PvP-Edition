using GameModes;

namespace BeaverBuddies.Connect
{
    /**
     * How many can play a hosted game, the host included: a game setting,
     * picked when the game is created and saved with it. Once that many are
     * in, the server turns anyone else away and the game is off the list.
     */
    public static class PlayerLimit
    {
        public const string SettingId = "bb.maxplayers";
        public const int Default = 4;
        private const int Fewest = 2, Most = 8;

        // A game's limit is read from its record (Setting.Of): a save from before it was one has the default
        public static SliderSetting Setting { get; private set; }

        public static void AddGameSetting(SettingGroup group) =>
            Setting = group.Slider(SettingId, "BeaverBuddies.Settings.MaxPlayers", Fewest, Most, 1, Default);
    }
}

using System.Collections.Generic;
using BeaverBuddies.NewGame;

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

        public static void AddGameSetting() => GameSettings.Add(new GameSetting
        {
            Id = SettingId,
            GroupLocKey = "BeaverBuddies.Menu.Multiplayer",
            LabelLocKey = "BeaverBuddies.Settings.MaxPlayers",
            TooltipLocKey = "BeaverBuddies.Settings.MaxPlayers.Tooltip",
            Min = Fewest, Max = Most, Step = 1, Default = Default.ToString(),
            ValueLocKey = "BeaverBuddies.Settings.MaxPlayers.Value",
            Modes = new[] { GameSettings.WithOthers },
        });

        // A game's limit from its settings; a save from before it was one has the default
        public static int Of(IReadOnlyDictionary<string, string> settings) =>
            settings != null && settings.TryGetValue(SettingId, out string value) && int.TryParse(value, out int most) && most >= Fewest ? most : Default;

        // The loaded game's
        public static int Current => Of(GameRecord.Current);
    }
}

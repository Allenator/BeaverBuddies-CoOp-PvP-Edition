using BeaverBuddies.Connect;
using BeaverBuddies.DevTools;
using BeaverBuddies.Matchmaking;
using GameModes;
using UnityEngine.UIElements;

namespace BeaverBuddies.NewGame
{
    /**
     * What BeaverBuddies adds to the New Game screens through the Game Modes
     * mod: its game settings under the Multiplayer heading (for a game with
     * others), the starting locations row among them, and what it keeps and
     * says about a game beyond its mode and settings.
     */
    public static class MultiplayerRules
    {
        public const string Heading = "BeaverBuddies.Menu.Multiplayer";
        private const string PlayersRow = "PlayersWrapper";

        // On the main thread, for the settings' PlayerPrefs
        public static void Register()
        {
            SettingGroup group = GameSettings.Group(Heading).When(kind => kind.WithOthers);
            DevToolsRule.AddGameSetting(group);
            PlayerLimit.AddGameSetting(group);

            // BeaverBuddies' own row in the game's list (see MultiStart): it goes under its heading, in the game's style
            GameSettings.AddRow(Heading, (list, ui) =>
            {
                VisualElement players = list.Q(PlayersRow);
                players?.RemoveFromHierarchy();
                if (players == null) return null;
                Label label = players.Q<Label>();
                if (label != null) label.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Normal;
                if (players.userData == null)
                {
                    ui.Tip(players, "BeaverBuddies.NewGame.MaxStartingLocations.Tooltip");
                    players.userData = true;
                }
                return players;
            });

            // A game made to host goes through events, even alone, when a mod runs players on this machine
            Modes.GameLoading += (record, _) => AloneHosting.Starting(record);

            // What a new game was made with beyond its mode and settings, kept with it and shown with it
            GameRecord.Building += record =>
            {
                foreach (var pair in MatchOptions.ForGameOptions) record[pair.Key] = pair.Value;
            };
            GameRecord.Describers.Add((loc, record) => MatchOptions.Lines(loc, record));
        }
    }
}

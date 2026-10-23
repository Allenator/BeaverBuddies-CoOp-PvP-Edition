using System;
using System.Collections.Generic;
using System.Linq;
using GameModes;
using Timberborn.Localization;

namespace BeaverBuddies.Matchmaking
{
    /**
     * What a multiplayer game is picked as when it's hosted or matched: its game mode (GameModes.Modes,
     * picked in the mode column for games with others), as the dictionary a match pairs players on
     * (only players who picked the same are paired) and hosted games list. The game that starts
     * hands it back to the mods as it loads (GameValue).
     */
    public static class MatchOptions
    {
        // The game mode, among the picks under the key it's recorded by
        public const string GameModeId = Modes.RecordKey;
        // BeaverBuddies' own: everyone building one settlement together
        public const string CoOp = "bb.coop";

        public static void AddCoOp()
        {
            Modes.Add(CoOp, "BeaverBuddies.Match.GameMode.CoOp", alone: false);
            Modes.Describe(CoOp, "BeaverBuddies.Match.GameMode.CoOp.Description");
            // The mode a game with others starts on, whichever mod adds its modes first
            Modes.SetDefault(CoOp, alone: false);
        }

        // Whether an id is one of the picks
        public static bool IsPick(string id) => id == GameModeId;

        // What this player picked last time for a game with others
        public static string ValueOf(string id) => id == GameModeId ? Modes.PickedMode(alone: false) : null;

        public static void Pick(string id, string value)
        {
            if (id == GameModeId) Modes.Pick(value, alone: false);
        }

        /** The picks as this player has them set. */
        public static Dictionary<string, string> Current() =>
            ValueOf(GameModeId) is { } mode ? new Dictionary<string, string> { [GameModeId] = mode } : new Dictionary<string, string>();

        /** The picks as a key a search can match exactly. */
        public static string Key(Dictionary<string, string> options) => options == null ? "" :
            string.Join(";", options.OrderBy(o => o.Key, StringComparer.Ordinal).Select(o => o.Key + "=" + o.Value));

        // A game with others, with these picks: the mode goes to GameModes
        public static void StartGameWith(Dictionary<string, string> options)
        {
            Modes.StartWith(options != null && options.TryGetValue(GameModeId, out string mode) ? mode : null, alone: false);
            Plugin.Log($"Starting a game with options: {Key(options)}");
        }

        /** The pick for the game starting on this machine, or null if it wasn't started with one. */
        public static string GameValue(string id) => id == GameModeId ? Modes.Game : null;

        /** The picks by their values alone: Horde, PvP. */
        public static IEnumerable<string> Labels(ILoc loc, Dictionary<string, string> options)
        {
            if (Modes.ModeOf(options) is { } mode) yield return Modes.Label(loc, mode);
        }

        /** The picks in words, one per line. */
        public static string Describe(ILoc loc, Dictionary<string, string> options) => string.Join("\n", GameRecord.Describe(loc, options));
    }
}

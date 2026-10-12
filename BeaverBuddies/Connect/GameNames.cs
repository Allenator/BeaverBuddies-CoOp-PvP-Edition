using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Newtonsoft.Json.Linq;
using Timberborn.SaveMetadataSystem;
using Timberborn.Persistence;

namespace BeaverBuddies.Connect
{
    /**
     * The name of the save a multiplayer game is loaded from: the map, the
     * host and, in a match, the other player ("Crossroads Ada vs Bo"), with
     * a number if the host already has a save by that name. The host picks it
     * when the game starts and writes it into the save's metadata, so every
     * player names their copy the same, and a resync keeps it.
     */
    public static class GameNames
    {
        public const string Fallback = "Multiplayer game";
        internal const string MetadataName = "BeaverBuddies.GameName";
        internal static readonly ListKey<string> MetadataKey = new(MetadataName);

        // The host's, while it's hosting
        public static string Hosted { get; private set; }

        public static string Current(Func<string, bool> taken) => Hosted ?? Begin(taken);

        public static void Reset() => Hosted = null;

        public static string Begin(Func<string, bool> taken)
        {
            string map = SingletonManager.GetSingleton<ReplayService>()?.ServerMapName;
            string name = (map + " " + Players.PlayerIdentity.LocalName).Trim();
#if IS_STEAM
            var state = Matchmaking.MatchmakingSession.State;
            string opponent = Matchmaking.MatchmakingSession.OpponentName;
            bool inMatch = state == Matchmaking.MatchState.Starting || state == Matchmaking.MatchState.Hosting;
            if (inMatch && !string.IsNullOrEmpty(opponent)) name += " vs " + opponent;
#endif
            name = Clean(name) ?? Fallback;
            string numbered = name;
            for (int n = 2; taken(numbered); n++) numbered = name + " " + n;
            Hosted = numbered;
            return Hosted;
        }

        // The name the host wrote into a save it sent
        public static string FromSave(byte[] saveFile)
        {
            try
            {
                using var zip = new ZipArchive(new MemoryStream(saveFile), ZipArchiveMode.Read);
                var entry = zip.GetEntry("save_metadata.json");
                if (entry == null) return Fallback;
                using var reader = new StreamReader(entry.Open());
                JToken lines = JObject.Parse(reader.ReadToEnd())[MetadataName];
                string name = lines is JArray array ? (string)array.FirstOrDefault() : null;
                return Clean(name) ?? Fallback;
            }
            catch (Exception)
            {
                return Fallback;
            }
        }

        private static string Clean(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            string clean = new string(name.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray()).Trim();
            return clean.Length == 0 ? null : clean;
        }
    }

    // Into the save's metadata, for the players who get the save
    [HarmonyLib.HarmonyPatch(typeof(SaveMetadataSerializer), "SaveMetadata")]
    class GameNameMetadataPatcher
    {
        static void Postfix(IObjectSaver objectSaver)
        {
            if (GameNames.Hosted != null) objectSaver.Set(GameNames.MetadataKey, new System.Collections.Generic.List<string> { GameNames.Hosted });
        }
    }
}

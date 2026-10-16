using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BeaverBuddies.Players
{
    /**
     * Every player picks a preferred color from a fixed set, and each match
     * gives every player a different one. Each copy of the game works it out
     * on its own from what PlayerPresence hears, the same way: players in ID
     * order get their preferred color if it's free, then the first free one.
     */
    public static class PlayerColors
    {
        public static readonly (string Key, Color Color)[] Palette =
        {
            ("Yellow", new Color(0.98f, 0.82f, 0.15f)),
            ("Red", new Color(0.85f, 0.15f, 0.12f)),
            ("Blue", new Color(0.18f, 0.45f, 0.92f)),
            ("Green", new Color(0.25f, 0.75f, 0.25f)),
            ("Orange", new Color(0.96f, 0.52f, 0.1f)),
            ("Purple", new Color(0.6f, 0.3f, 0.85f)),
            ("Cyan", new Color(0.2f, 0.82f, 0.85f)),
            ("Pink", new Color(0.95f, 0.45f, 0.7f)),
            ("White", new Color(0.95f, 0.95f, 0.95f)),
            ("Brown", new Color(0.55f, 0.35f, 0.2f)),
        };

        public static int Clamp(int index) => index >= 0 && index < Palette.Length ? index : 0;

        /** Each player's color index, from their ID and preferred index. */
        public static Dictionary<string, int> Assign(IEnumerable<KeyValuePair<string, int>> preferences)
        {
            var assigned = new Dictionary<string, int>();
            var taken = new HashSet<int>();
            var ordered = preferences.OrderBy(p => p.Key, System.StringComparer.Ordinal).ToList();
            foreach (var (id, preferred) in ordered.Select(p => (p.Key, Clamp(p.Value))))
            {
                if (taken.Add(preferred)) assigned[id] = preferred;
            }
            foreach (var (id, preferred) in ordered.Select(p => (p.Key, Clamp(p.Value))))
            {
                if (assigned.ContainsKey(id)) continue;
                int free = Enumerable.Range(0, Palette.Length).FirstOrDefault(i => !taken.Contains(i));
                // More players than colors: share the preferred one
                int pick = taken.Count < Palette.Length ? free : preferred;
                taken.Add(pick);
                assigned[id] = pick;
            }
            return assigned;
        }
    }
}

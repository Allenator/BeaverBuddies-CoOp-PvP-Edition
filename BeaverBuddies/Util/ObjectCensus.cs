using System.Linq;
using UnityEngine;

namespace BeaverBuddies.Util
{
    /**
     * What Unity holds, by type, to compare one load of the game with the
     * next: a game that is loaded over another one should leave nothing of
     * the first behind, and the load takes longer each time when it does.
     */
    public static class ObjectCensus
    {
        public static void Log(string when)
        {
            var objects = UnityEngine.Resources.FindObjectsOfTypeAll<Object>();
            var top = objects.GroupBy(o => o.GetType().Name)
                .OrderByDescending(g => g.Count())
                .Take(14)
                .Select(g => $"{g.Key} {g.Count()}");
            Plugin.Log($"Unity objects {when}: {objects.Length}; {string.Join(", ", top)}");
            var materials = objects.OfType<Material>()
                .GroupBy(m => m.name)
                .OrderByDescending(g => g.Count())
                .Take(10)
                .Select(g => $"{g.Key} {g.Count()}");
            Plugin.Log($"Materials {when}: {string.Join(", ", materials)}");
        }
    }
}

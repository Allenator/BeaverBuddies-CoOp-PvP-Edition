using HarmonyLib;
using System.Collections.Generic;
using Timberborn.Navigation;

namespace BeaverBuddies.Fixes
{
    /**
     * Where two districts' roads join, with no District Crossing between them, the game's district map throws as it
     * gives each road to its district: in a tick, so the game stops, and on every machine at once, as the district
     * map is the same on all of them. The building tool keeps a player from joining
     * them (see DistrictPreviewFix), but a game that gets there anyway plays on in a Co-op game: a road both
     * districts reach stays with the district the map took first, the same one on every machine, as the map takes
     * them in the same order on all of them, and the log says which two districts are joined.
     */
    [ManualMethodOverwrite]
    /*
        10/26/2026
        private void AssignDistrictToRoadMap(District district)
        {
            foreach (int allNodeId in _districtRoadFlowFields[district].GetAllNodeIds())
            {
                if (_districtsOnRoads.ContainsKey(allNodeId))
                {
                    throw new InvalidOperationException($"District {district} conflicts with district {_districtsOnRoads[allNodeId]}");
                }
                _districtsOnRoads[allNodeId] = district;
            }
        }
     */
    [HarmonyPatch(typeof(DistrictMap), nameof(DistrictMap.AssignDistrictToRoadMap))]
    static class DistrictConflictPatcher
    {
        // Said once for each pair in a game's map: the map is worked out again as roads change
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DistrictMap, HashSet<(int, int)>> said = new();

        static bool Prefix(DistrictMap __instance, District district)
        {
            if (IO.EventIO.IsNull) return true;
            foreach (int nodeId in __instance._districtRoadFlowFields[district].GetAllNodeIds())
            {
                if (__instance._districtsOnRoads.TryGetValue(nodeId, out District other))
                {
                    if (other != district && said.GetOrCreateValue(__instance).Add((other.CenterNodeId, district.CenterNodeId)))
                    {
                        Plugin.LogWarning($"District {district} and district {other} are joined by roads with no District Crossing between them: the roads they share stay with {other}");
                    }
                    continue;
                }
                __instance._districtsOnRoads[nodeId] = district;
            }
            return false;
        }
    }
}

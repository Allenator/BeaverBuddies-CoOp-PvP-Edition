using HarmonyLib;
using Timberborn.Navigation;

namespace BeaverBuddies.Fixes
{
    /**
     * The building tool checks a new district center, or a road, against the preview district map: one that would
     * join two districts' roads is refused ("Districts in conflict"). A district center's preview puts a district of
     * its own in that map as it's shown (PreviewDistrictAdder) and takes it out as it goes. Alone, the building is
     * placed on the click and its preview goes with it, before the district center's own district reaches the map.
     * In a Co-op game the placement comes back from the host while the preview is still up: the preview's district
     * holds the spot, adding the real one throws (swallowed, see DistrictBuildingsFix), and taking the preview's out
     * takes the spot out. Until the game is loaded again, the player who placed it has no district center there in
     * that map, and can place a second one on roads that join the first: when they join in the game, every copy's
     * district map throws, and the game crashes on every machine.
     *
     * In the preview district map, a district added where one is already takes its place, and a district taken out
     * after it was replaced leaves the spot to the one that replaced it.
     */
    [HarmonyPatch(typeof(DistrictMap), nameof(DistrictMap.AddDistrictCenter))]
    static class PreviewDistrictAddPatcher
    {
        static void Prefix(DistrictMap __instance, District district)
        {
            if (IO.EventIO.IsNull || __instance is not PreviewDistrictMap) return;
            if (!__instance._districtCenters.TryGetValue(district.CenterNodeId, out District there) || there == district) return;
            __instance._districtCenters.Remove(district.CenterNodeId);
            __instance._districtRoadFlowFields.Remove(there);
            __instance._districtRoadSpillFlowFields.Remove(there);
        }
    }

    [HarmonyPatch(typeof(DistrictMap), nameof(DistrictMap.RemoveDistrictCenter))]
    static class PreviewDistrictRemovePatcher
    {
        static bool Prefix(DistrictMap __instance, District district)
        {
            if (IO.EventIO.IsNull || __instance is not PreviewDistrictMap) return true;
            if (!__instance._districtCenters.TryGetValue(district.CenterNodeId, out District there) || there == district) return true;
            __instance._districtRoadFlowFields.Remove(district);
            __instance._districtRoadSpillFlowFields.Remove(district);
            return false;
        }
    }
}

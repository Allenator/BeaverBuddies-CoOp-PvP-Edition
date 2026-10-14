using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using Timberborn.FactionSystem;
using Timberborn.GameFactionSystem;
using Timberborn.TemplateCollectionSystem;

namespace BeaverBuddies.Modding
{
    /**
     * BeaverBuddies' buildings for the faction played: the template
     * collection BeaverBuddies.Buildings.<faction>. A mod with factions
     * of its own adds the collection of the faction each one builds on.
     */
    [HarmonyPatch(typeof(FactionTemplateCollectionIdProvider), nameof(FactionTemplateCollectionIdProvider.GetTemplateCollectionIds))]
    class FactionBuildingsPatcher
    {
        public const string Prefix = "BeaverBuddies.Buildings.";

        static void Postfix(FactionTemplateCollectionIdProvider __instance, ref IEnumerable<string> __result)
        {
            string faction = __instance._factionService.Current?.Id;
            if (faction == null) return;
            __result = __result.Append(Prefix + faction).Distinct().ToList();
        }
    }
}

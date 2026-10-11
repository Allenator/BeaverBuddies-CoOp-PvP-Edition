using HarmonyLib;
using System;
using Timberborn.EntitySystem;
using Timberborn.TemplateSystem;

namespace BeaverBuddies.Fixes
{
    /**
     * The game numbers every template it instantiates from one counter, and
     * breaks ties by that number: which of two equally near trees a gatherer
     * goes to, which of two workplaces with the same priority is served first.
     * Everything instantiated takes a number, the previews and ghosts the
     * building and planting tools make as a player moves the cursor, and
     * those exist on one machine only, so the numbers drift apart and ties
     * go different ways on different machines.
     *
     * An entity's number now comes from its own ID, which every machine
     * agrees on whatever it has instantiated before (or loaded from a save),
     * and the objects that aren't entities take none.
     */
    [HarmonyPatch(typeof(InstantiatedTemplate), nameof(InstantiatedTemplate.Awake))]
    public class InstantiationOrderFix
    {
        static bool Prefix(InstantiatedTemplate __instance)
        {
            if (IO.EventIO.IsNull) return true;
            var entity = __instance.GetComponent<EntityComponent>();
            __instance.InstantiationOrder = entity ? OrderOf(entity.EntityId) : 0;
            return false;
        }

        // Positive, and as spread out as the ID is
        public static int OrderOf(Guid id)
        {
            byte[] b = id.ToByteArray();
            int h = BitConverter.ToInt32(b, 0) ^ BitConverter.ToInt32(b, 4) ^ BitConverter.ToInt32(b, 8) ^ BitConverter.ToInt32(b, 12);
            return h & 0x7FFFFFFF;
        }
    }
}

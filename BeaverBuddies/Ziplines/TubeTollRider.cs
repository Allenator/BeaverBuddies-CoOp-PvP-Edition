using Timberborn.BaseComponentSystem;
using Timberborn.Persistence;
using Timberborn.WorldPersistence;
using UnityEngine;

using BeaverBuddies.Events;
using BeaverBuddies.Modding;
using BeaverBuddies.Players;
using BeaverBuddies.Util;

namespace BeaverBuddies.Ziplines
{
    /**
     * The tile a beaver or bot was on when the tube tracker last checked
     * it (see TubeTrackerTollPatcher). Saved, so the first move after a
     * load charges the same toll as it would have without the load.
     */
    public class TubeTollRider : BaseComponent, IPersistentEntity
    {
        private static readonly ComponentKey Key = new("BeaverBuddies.TubeTollRider");
        private static readonly PropertyKey<Vector3Int> LastKey = new("Last");

        // None until the tracker first checks it, as before saving
        public Vector3Int? Last { get; set; }

        public void Save(IEntitySaver entitySaver)
        {
            if (Last.HasValue) entitySaver.GetComponent(Key).Set(LastKey, Last.Value);
        }

        public void Load(IEntityLoader entityLoader)
        {
            if (entityLoader.TryGetComponent(Key, out IObjectLoader loader) && loader.Has(LastKey))
            {
                Last = loader.Get(LastKey);
            }
        }
    }
}

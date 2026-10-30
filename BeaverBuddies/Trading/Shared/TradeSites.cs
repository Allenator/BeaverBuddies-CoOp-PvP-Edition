// Copied from Archipelago/Archipelago/Trade/Shared by Tools/sync_trade.py: edit it there and run the script.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Timberborn.BaseComponentSystem;
using Timberborn.GameDistricts;

namespace BeaverBuddies.Trading
{
    /**
     * A trade post as the Trade tab and the post's panel see it, whichever mod it comes from. Only the system's and
     * the game's types go in or out: Archipelago and BeaverBuddies each carry a copy of this code, and the one whose
     * tab shows reaches the other's posts by these names.
     */
    public interface ITradeSite
    {
        Guid Id { get; }
        BaseComponent Entity { get; }
        // The tab's header for it: its name, whose it is when it isn't yours, and whether it's built yet
        string Title { get; }
        // Where it goes in the tab's list
        string SortKey { get; }
        DistrictCenter District { get; }
        bool Finished { get; }
        // Whether this player may change its rules
        bool Editable { get; }
        // Goes up with every change to its rules
        int Version { get; }
        // What the tab says above its goods
        string Help { get; }

        bool Takes(string goodId);
        // The rule for a good, as mode, value and limit
        ValueTuple<int, int, int> RuleOf(string goodId);
        // Sets a good's rule on every machine
        void SendRule(string goodId, int mode, int value, int limit);
        // The number shown by a good: what it has to sell, or what it already has of what it buys
        int Shown(string goodId);
        string ShownTooltip(string goodId);
        string LimitTooltip(string goodId);
    }

    /**
     * Where a mod's posts meet the other mod's. Each copy registers its own posts and how to open its tab; the copy
     * with the highest rank shows the one Trade tab, with every post from both.
     */
    public static class TradeHub
    {
        private static readonly string[] Copies = { "Archipelago.Trade.TradeHub", "BeaverBuddies.Trading.TradeHub" };

        private static Func<IEnumerable<ITradeSite>> _sites;
        private static Action<BaseComponent> _open;
        private static Type[] _others;

        // Which copy shows the tab when both mods are on: BeaverBuddies', next to its Power Exchange and toll stations
        public static int Rank => TradeText.Rank;

        public static void Register(Func<IEnumerable<ITradeSite>> sites, Action<BaseComponent> open)
        {
            _sites = sites;
            _open = open;
        }

        public static void Unregister()
        {
            _sites = null;
            _open = null;
        }

        // ---- What the other copy calls, by name ----

        public static IEnumerable<object> LocalSites() => _sites?.Invoke().Cast<object>() ?? Enumerable.Empty<object>();

        public static bool OpenLocal(BaseComponent entity)
        {
            if (_open == null) return false;
            _open(entity);
            return true;
        }

        // ---- What this copy's own code calls ----

        /** Whether this copy shows the Trade tab: no other copy outranks it. */
        public static bool Hosts => Others().All(t => (int)t.GetProperty("Rank").GetValue(null) < Rank);

        /** Every post, this mod's and the other's. */
        public static IEnumerable<ITradeSite> Sites()
        {
            foreach (ITradeSite site in _sites?.Invoke() ?? Enumerable.Empty<ITradeSite>()) yield return site;
            foreach (Type other in Others())
            {
                var foreign = (IEnumerable<object>)other.GetMethod("LocalSites").Invoke(null, null);
                foreach (object site in foreign) yield return new ForeignSite(site);
            }
        }

        /** Opens the Trade tab on a post, in whichever mod shows it. */
        public static void Open(BaseComponent entity)
        {
            if (!Hosts)
            {
                Type host = Others().OrderByDescending(t => (int)t.GetProperty("Rank").GetValue(null)).First();
                if ((bool)host.GetMethod("OpenLocal").Invoke(null, new object[] { entity })) return;
            }
            _open?.Invoke(entity);
        }

        // The other mod's copies, looked up once
        private static Type[] Others() => _others ??= AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a != typeof(TradeHub).Assembly)
            .SelectMany(a => Copies.Select(name => SafeType(a, name)))
            .Where(t => t != null).ToArray();

        private static Type SafeType(Assembly assembly, string name)
        {
            try
            {
                return assembly.GetType(name, false);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /** The other mod's post, through its own copy of ITradeSite. */
        private class ForeignSite : ITradeSite
        {
            private readonly object _site;
            private readonly Type _type;

            public ForeignSite(object site)
            {
                _site = site;
                _type = site.GetType();
            }

            private T Get<T>(string name) => (T)_type.GetProperty(name).GetValue(_site);

            private T Call<T>(string name, params object[] args) => (T)_type.GetMethod(name).Invoke(_site, args);

            public Guid Id => Get<Guid>(nameof(Id));
            public BaseComponent Entity => Get<BaseComponent>(nameof(Entity));
            public string Title => Get<string>(nameof(Title));
            public string SortKey => Get<string>(nameof(SortKey));
            public DistrictCenter District => Get<DistrictCenter>(nameof(District));
            public bool Finished => Get<bool>(nameof(Finished));
            public bool Editable => Get<bool>(nameof(Editable));
            public int Version => Get<int>(nameof(Version));
            public string Help => Get<string>(nameof(Help));
            public bool Takes(string goodId) => Call<bool>(nameof(Takes), goodId);
            public ValueTuple<int, int, int> RuleOf(string goodId) => Call<ValueTuple<int, int, int>>(nameof(RuleOf), goodId);
            public void SendRule(string goodId, int mode, int value, int limit) => _type.GetMethod(nameof(SendRule)).Invoke(_site, new object[] { goodId, mode, value, limit });
            public int Shown(string goodId) => Call<int>(nameof(Shown), goodId);
            public string ShownTooltip(string goodId) => Call<string>(nameof(ShownTooltip), goodId);
            public string LimitTooltip(string goodId) => Call<string>(nameof(LimitTooltip), goodId);
        }
    }

    public static class TradeSites
    {
        public static TradeRule Rule(this ITradeSite site, string goodId)
        {
            var (mode, value, limit) = site.RuleOf(goodId);
            return new TradeRule((TradeMode)mode, value, limit);
        }

        public static void Send(this ITradeSite site, string goodId, TradeRule rule) => site.SendRule(goodId, (int)rule.Mode, rule.Value, rule.Limit);
    }
}

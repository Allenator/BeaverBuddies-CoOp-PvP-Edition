// Copied from Archipelago/Archipelago/Trade/Shared by Tools/sync_trade.py: edit it there and run the script.
using System;
using System.Collections.Generic;
using System.Linq;

namespace BeaverBuddies.Trading
{
    public enum TradeMode { Off, Buy, Sell }

    /** What a trade post does with a good: buys it or sells it, what one is worth, and how far it goes. */
    public readonly struct TradeRule : IEquatable<TradeRule>
    {
        // What a good starts at once it's turned on, and the most a post goes to
        public const int DefaultValue = 1;
        public const int DefaultSellLimit = 30;
        public const int DefaultBuyLimit = 100;
        public const int MostValue = 99;
        public const int MostLimit = 999;

        public readonly TradeMode Mode;
        // What one is worth against the post's other goods, 1 or more
        public readonly int Value;
        // Selling: how many the post keeps in stock to sell. Buying: it buys until its stock has this many
        public readonly int Limit;

        public TradeRule(TradeMode mode, int value, int limit)
        {
            Mode = mode;
            Value = Math.Min(MostValue, Math.Max(1, value));
            Limit = Math.Min(MostLimit, Math.Max(0, limit));
        }

        public static readonly TradeRule Off = new(TradeMode.Off, DefaultValue, 0);

        public bool Equals(TradeRule other) => Mode == other.Mode && Value == other.Value && Limit == other.Limit;

        public override bool Equals(object obj) => obj is TradeRule other && Equals(other);

        public override int GetHashCode() => ((int)Mode * 397 + Value) * 1009 + Limit;
    }

    /** A post's rules, a good each, with a version that goes up on every change for the panels to redraw by. */
    public class TradeBook
    {
        private readonly SortedDictionary<string, TradeRule> _rules = new(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, TradeRule> Rules => _rules;
        public int Version { get; private set; }

        public TradeRule RuleFor(string goodId) => _rules.TryGetValue(goodId, out TradeRule rule) ? rule : TradeRule.Off;

        /** Sets what it does with a good: off forgets it. Returns whether anything changed. */
        public bool Set(string goodId, TradeRule rule)
        {
            if (rule.Mode == TradeMode.Off)
            {
                if (!_rules.Remove(goodId)) return false;
            }
            else
            {
                if (_rules.TryGetValue(goodId, out TradeRule was) && was.Equals(rule)) return false;
                _rules[goodId] = rule;
            }
            Version++;
            return true;
        }

        // Haulers keep it stocked with what it sells; anything else in it is unwanted and they carry it off
        public int AllowedAmount(string goodId) => _rules.TryGetValue(goodId, out TradeRule rule) && rule.Mode == TradeMode.Sell ? rule.Limit : 0;

        /** How much more of a good it buys, with this much of it already in its stock. */
        public int BuyRoom(string goodId, int stock) =>
            _rules.TryGetValue(goodId, out TradeRule rule) && rule.Mode == TradeMode.Buy ? Math.Max(0, rule.Limit - stock) : 0;

        // Saved as four lists of the same length
        public List<string> Goods => _rules.Keys.ToList();
        public List<int> Modes => _rules.Values.Select(r => (int)r.Mode).ToList();
        public List<int> Values => _rules.Values.Select(r => r.Value).ToList();
        public List<int> Limits => _rules.Values.Select(r => r.Limit).ToList();

        public void Load(IList<string> goods, IList<int> modes, IList<int> values, IList<int> limits)
        {
            for (int i = 0; i < goods.Count && i < modes.Count && i < values.Count && i < limits.Count; i++)
            {
                Set(goods[i], new TradeRule((TradeMode)modes[i], values[i], limits[i]));
            }
        }
    }
}

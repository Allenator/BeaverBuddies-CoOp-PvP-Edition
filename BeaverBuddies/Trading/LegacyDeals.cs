using System;
using System.Collections.Generic;

namespace BeaverBuddies.Trading
{
    /**
     * Trading posts from before each half had its own rules: the two sides agreed on deals, so many of one good for
     * so many of another. A side that agreed to a deal now sells what it gave and buys what it got, at values that
     * keep the deal's rate. The first deal to set a good wins.
     */
    public static class LegacyDeals
    {
        /**
         * The rules each side gets from the saved deals: id, A's good, A's amount, B's good, B's amount, A agreed,
         * B agreed, tab-separated.
         */
        public static (Dictionary<string, TradeRule> a, Dictionary<string, TradeRule> b) FromDeals(IEnumerable<string> deals)
        {
            var a = new Dictionary<string, TradeRule>(StringComparer.Ordinal);
            var b = new Dictionary<string, TradeRule>(StringComparer.Ordinal);
            foreach (string value in deals)
            {
                string[] parts = value.Split('\t');
                if (parts.Length != 7) continue;
                int aAmount = Amount(parts[2]), bAmount = Amount(parts[4]);
                if (parts[5] == bool.TrueString) Agree(a, parts[1], aAmount, parts[3], bAmount);
                if (parts[6] == bool.TrueString) Agree(b, parts[3], bAmount, parts[1], aAmount);
            }
            return (a, b);
        }

        /** The rules a half gets from its own older list of trades: a good it gave, how many, a good it got, how many. */
        public static Dictionary<string, TradeRule> FromTrades(IEnumerable<string> trades)
        {
            var rules = new Dictionary<string, TradeRule>(StringComparer.Ordinal);
            foreach (string value in trades)
            {
                string[] parts = value.Split('\t');
                if (parts.Length != 4) continue;
                Agree(rules, parts[0], Amount(parts[1]), parts[2], Amount(parts[3]));
            }
            return rules;
        }

        private static int Amount(string text) => int.TryParse(text, out int amount) ? Math.Max(1, amount) : 1;

        // Giving so many of one good for so many of another: the first good's value is the second's amount
        private static void Agree(Dictionary<string, TradeRule> rules, string gives, int givesAmount, string gets, int getsAmount)
        {
            if (!string.IsNullOrEmpty(gives) && !rules.ContainsKey(gives))
            {
                rules[gives] = new TradeRule(TradeMode.Sell, getsAmount, Math.Max(TradeRule.DefaultSellLimit, givesAmount * 2));
            }
            if (!string.IsNullOrEmpty(gets) && gets != gives && !rules.ContainsKey(gets))
            {
                rules[gets] = new TradeRule(TradeMode.Buy, givesAmount, TradeRule.DefaultBuyLimit);
            }
        }
    }
}

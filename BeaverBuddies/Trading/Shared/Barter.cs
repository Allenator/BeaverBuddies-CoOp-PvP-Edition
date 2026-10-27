// Copied from Archipelago/Archipelago/Trade/Shared by Tools/sync_trade.py: edit it there and run the script.
using System;
using System.Collections.Generic;
using System.Linq;

namespace BeaverBuddies.Trading
{
    /**
     * One ship's trade at one post, worked out without the game: what goes ashore and what comes aboard.
     *
     * The ship gives what it carries beyond what it's set to load, of the goods the post buys, up to the
     * room the post has for each. It takes what it's set to load and hasn't got, of the goods the post
     * sells, up to the post's stock and the hold's room. Between one owner's ship and post that's all.
     * Between two owners the post's values balance it: the ship gives a good at a time until what it has
     * given pays for the next good it takes, and what it gave and never needed goes back aboard. So a ship
     * never gives more than one good's worth over what it gets, and gets nothing it hasn't paid for.
     */
    public static class Barter
    {
        public class Ship
        {
            // What is aboard, and what it's set to load
            public IReadOnlyDictionary<string, int> Aboard;
            public IReadOnlyDictionary<string, int> Loads;
            // How much more the hold takes, all goods together
            public int Room;
        }

        public class Post
        {
            public IReadOnlyDictionary<string, TradeRule> Rules;
            // What it has to sell, and how much more of each good it buys
            public IReadOnlyDictionary<string, int> Stock;
            public IReadOnlyDictionary<string, int> BuyRoom;
        }

        public class Result
        {
            public readonly SortedDictionary<string, int> Given = new(StringComparer.Ordinal);
            public readonly SortedDictionary<string, int> Taken = new(StringComparer.Ordinal);
            public bool Any => Given.Count > 0 || Taken.Count > 0;
        }

        public static Result Trade(Ship ship, Post post, bool sameOwner)
        {
            var offer = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var want = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var aboard in ship.Aboard)
            {
                if (!post.Rules.TryGetValue(aboard.Key, out TradeRule rule) || rule.Mode != TradeMode.Buy) continue;
                int spare = aboard.Value - Get(ship.Loads, aboard.Key);
                int amount = Math.Min(spare, Get(post.BuyRoom, aboard.Key));
                if (amount > 0) offer[aboard.Key] = amount;
            }
            foreach (var load in ship.Loads)
            {
                if (!post.Rules.TryGetValue(load.Key, out TradeRule rule) || rule.Mode != TradeMode.Sell) continue;
                int amount = Math.Min(load.Value - Get(ship.Aboard, load.Key), Get(post.Stock, load.Key));
                if (amount > 0) want[load.Key] = amount;
            }
            var result = new Result();
            int room = ship.Room;
            if (sameOwner)
            {
                foreach (var o in offer) result.Given[o.Key] = o.Value;
                // What goes ashore makes room for what comes aboard
                room += offer.Values.Sum();
                foreach (var w in want)
                {
                    int amount = Math.Min(w.Value, room);
                    if (amount <= 0) break;
                    result.Taken[w.Key] = amount;
                    room -= amount;
                }
                return result;
            }
            // One good at a time, round the goods in turn, so a trade spreads over all of them
            var gave = new List<string>();
            int credit = 0;
            var buyOrder = want.Keys.ToList();
            var sellOrder = offer.Keys.ToList();
            int nextBuy = 0, nextSell = 0;
            while (true)
            {
                string buy = Next(buyOrder, want, ref nextBuy);
                if (buy == null) break;
                int price = post.Rules[buy].Value;
                if (credit >= price && room > 0)
                {
                    credit -= price;
                    want[buy]--;
                    Add(result.Taken, buy, 1);
                    room--;
                    nextBuy++;
                    continue;
                }
                string sell = Next(sellOrder, offer, ref nextSell);
                if (sell == null) break;
                credit += post.Rules[sell].Value;
                offer[sell]--;
                Add(result.Given, sell, 1);
                gave.Add(sell);
                // A good given goes ashore and leaves room in the hold
                room++;
                nextSell++;
            }
            // What was given that nothing was taken for comes back, the last first
            for (int i = gave.Count - 1; i >= 0 && credit > 0; i--)
            {
                int value = post.Rules[gave[i]].Value;
                if (value > credit) continue;
                credit -= value;
                if (--result.Given[gave[i]] == 0) result.Given.Remove(gave[i]);
            }
            return result;
        }

        /**
         * Two posts trading with each other, each with its own rules: what one sells and the other buys goes
         * across, up to the seller's stock and the buyer's room. Between one owner's posts that's all. Between
         * two owners each post weighs the trade by its own values and takes it only if it gets at least what
         * it gives: B pays a good at a time, A sells for what it has been paid, and the trade stops at the
         * longest run of it both still agree to.
         */
        public static SwapResult Swap(Post a, Post b, bool sameOwner)
        {
            var ab = Across(a, b);
            var ba = Across(b, a);
            var result = new SwapResult();
            if (sameOwner)
            {
                foreach (var g in ab) result.AToB[g.Key] = g.Value;
                foreach (var g in ba) result.BToA[g.Key] = g.Value;
                return result;
            }
            // Each step one good, true going from A to B; the balances are what each side is up, by its own values
            var steps = new List<(string good, bool aToB)>();
            int balanceA = 0, balanceB = 0, best = 0;
            var abOrder = ab.Keys.ToList();
            var baOrder = ba.Keys.ToList();
            int nextAb = 0, nextBa = 0;
            while (true)
            {
                bool moved = false;
                // A sells for as long as what it has been paid covers the next good
                while (Next(abOrder, ab, ref nextAb) is string sell && balanceA >= a.Rules[sell].Value)
                {
                    balanceA -= a.Rules[sell].Value;
                    balanceB += b.Rules[sell].Value;
                    ab[sell]--;
                    nextAb++;
                    steps.Add((sell, true));
                    moved = true;
                    if (balanceA >= 0 && balanceB >= 0) best = steps.Count;
                }
                // Then B pays one more
                string pay = Next(baOrder, ba, ref nextBa);
                if (pay != null)
                {
                    balanceB -= b.Rules[pay].Value;
                    balanceA += a.Rules[pay].Value;
                    ba[pay]--;
                    nextBa++;
                    steps.Add((pay, false));
                    moved = true;
                    if (balanceA >= 0 && balanceB >= 0) best = steps.Count;
                }
                if (!moved) break;
            }
            for (int i = 0; i < best; i++) Add(steps[i].aToB ? result.AToB : result.BToA, steps[i].good, 1);
            return result;
        }

        public class SwapResult
        {
            public readonly SortedDictionary<string, int> AToB = new(StringComparer.Ordinal);
            public readonly SortedDictionary<string, int> BToA = new(StringComparer.Ordinal);
            public bool Any => AToB.Count > 0 || BToA.Count > 0;
        }

        // What one post sells that the other buys, as much as can go across
        private static SortedDictionary<string, int> Across(Post seller, Post buyer)
        {
            var goods = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var rule in seller.Rules)
            {
                if (rule.Value.Mode != TradeMode.Sell || !buyer.Rules.TryGetValue(rule.Key, out TradeRule buys) || buys.Mode != TradeMode.Buy) continue;
                int amount = Math.Min(Get(seller.Stock, rule.Key), Get(buyer.BuyRoom, rule.Key));
                if (amount > 0) goods[rule.Key] = amount;
            }
            return goods;
        }

        // The next good of the list with some left, from where it got to, round again from the start
        private static string Next(List<string> order, IDictionary<string, int> left, ref int at)
        {
            for (int i = 0; i < order.Count; i++)
            {
                int index = (at + i) % order.Count;
                if (left[order[index]] <= 0) continue;
                at = index;
                return order[index];
            }
            return null;
        }

        private static int Get(IReadOnlyDictionary<string, int> amounts, string good) => amounts.TryGetValue(good, out int amount) ? amount : 0;

        private static void Add(IDictionary<string, int> amounts, string good, int amount) =>
            amounts[good] = (amounts.TryGetValue(good, out int was) ? was : 0) + amount;
    }
}

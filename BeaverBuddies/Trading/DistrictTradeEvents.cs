using System;

using BeaverBuddies.Events;
using BeaverBuddies.Modding;
using BeaverBuddies.Players;
using BeaverBuddies.Util;

namespace BeaverBuddies.Trading
{
    public enum DealAction
    {
        Add,
        Edit,
        Approve,
        Remove,
    }

    /**
     * A change to one of a trading post's deals, made by a player acting
     * for one side. Every machine checks that the player can act for that
     * side (with the synced district owners), so a change someone isn't
     * allowed to make is dropped everywhere.
     */
    [Serializable]
    public class DistrictDealEvent : ReplayEvent, IOwnershipChecked
    {
        public string entityID;
        public TradeSide side;
        public DealAction action;
        public int dealId;
        public string gives;
        public int givesAmount;
        public string gets;
        public int getsAmount;

        public override void Replay(IReplayContext context)
        {
            Apply(GetComponent<DistrictTrade>(context, entityID), playerID);
        }

        private void Apply(DistrictTrade trade, string actingPlayer)
        {
            DistrictTrade half = trade?.GetHalf(side);
            if (half == null) return;
            if (!half.CanEdit(actingPlayer))
            {
                Plugin.LogWarning($"Ignoring {action} on trading post {entityID}: {actingPlayer} can't act for side {side}");
                return;
            }

            TradeDeal deal = trade.FindDeal(dealId);
            // The deal this side now agrees to, if any
            TradeDeal agreed = null;
            switch (action)
            {
                case DealAction.Add:
                    agreed = trade.AddDeal(side, gives, givesAmount, gets, getsAmount);
                    break;
                case DealAction.Edit:
                    deal?.SetTerms(side, gives, givesAmount, gets, getsAmount);
                    agreed = deal;
                    break;
                case DealAction.Approve:
                    // Only the terms the player saw: an edit from the other
                    // side that played first needs approving again
                    if (deal != null && deal.IsConfigured && deal.Gives(side) == gives && deal.GivesAmount(side) == givesAmount
                        && deal.Gets(side) == gets && deal.GetsAmount(side) == getsAmount)
                    {
                        deal.SetApproved(side, true);
                        agreed = deal;
                    }
                    break;
                case DealAction.Remove:
                    trade.RemoveDeal(dealId);
                    break;
            }
            // A player who can act for both sides (both districts theirs, or
            // nobody's) agrees for both: the Trades tab only ever lets them act
            // for one, so the deal would never run
            TradeSide other = TradeDeal.Other(side);
            if (agreed != null && trade.GetHalf(other) is DistrictTrade otherHalf && otherHalf.CanEdit(actingPlayer))
            {
                agreed.SetApproved(other, true);
            }
        }

        public override string ToActionString()
        {
            return $"Trading post {entityID}, side {side}: {action} deal {dealId} ({givesAmount} {gives} for {getsAmount} {gets})";
        }

        /**
         * Makes a change through the event system in co-op, or directly
         * otherwise.
         */
        public static void Send(DistrictTrade trade, DistrictDealEvent change)
        {
            change.entityID = ReplayEvent.GetEntityID(trade);
            if (change.entityID == null) return;
            if (ReplayEvent.DoPrefix(() => change))
            {
                // Not replayed, so playerID was never filled in: it's this player's
                change.Apply(trade, change.playerID ?? PlayerIdentity.LocalID);
            }
        }
    }
}

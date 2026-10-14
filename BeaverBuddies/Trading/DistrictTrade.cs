using System;
using System.Collections.Generic;
using System.Linq;
using Timberborn.BaseComponentSystem;
using Timberborn.BlockSystem;
using Timberborn.BlueprintSystem;
using Timberborn.EntitySystem;
using Timberborn.GameDistricts;
using Timberborn.Goods;
using Timberborn.InventorySystem;
using Timberborn.LinkedBuildingSystem;
using Timberborn.Persistence;
using Timberborn.Stockpiles;
using Timberborn.TickSystem;
using Timberborn.TimeSystem;
using Timberborn.WorldPersistence;
using UnityEngine;

using BeaverBuddies.Events;
using BeaverBuddies.Modding;
using BeaverBuddies.Players;
using BeaverBuddies.Util;

namespace BeaverBuddies.Trading
{
    public record DistrictTradeSpec : ComponentSpec
    {
    }

    public enum TradeStatus
    {
        NotConfigured,
        NotConnected,
        AwaitingApproval,
        MissingGoodsHere,
        MissingGoodsThere,
        NoSpaceHere,
        NoSpaceThere,
        Traded,
    }

    public enum TradeSide
    {
        // The half that stores the deals and runs them
        A,
        B,
    }

    /**
     * One deal of a trading post: side A gives aAmount of aGood and side B
     * gives bAmount of bGood. It runs only once both sides approved it.
     */
    [Serializable]
    public class TradeDeal
    {
        public const int DefaultAmount = 1;
        private const char Separator = '\t';

        public int id;
        public string aGood;
        public int aAmount = DefaultAmount;
        public string bGood;
        public int bAmount = DefaultAmount;
        public bool aApproved;
        public bool bApproved;

        public bool IsConfigured => !string.IsNullOrEmpty(aGood) && !string.IsNullOrEmpty(bGood)
            && aGood != bGood && aAmount > 0 && bAmount > 0;

        public bool IsApproved => aApproved && bApproved;

        public string Gives(TradeSide side) => side == TradeSide.A ? aGood : bGood;
        public int GivesAmount(TradeSide side) => side == TradeSide.A ? aAmount : bAmount;
        public string Gets(TradeSide side) => side == TradeSide.A ? bGood : aGood;
        public int GetsAmount(TradeSide side) => side == TradeSide.A ? bAmount : aAmount;
        public bool Approved(TradeSide side) => side == TradeSide.A ? aApproved : bApproved;

        public void SetApproved(TradeSide side, bool approved)
        {
            if (side == TradeSide.A) aApproved = approved;
            else bApproved = approved;
        }

        /**
         * Sets what a side gives and gets. The other side has to approve
         * the new terms again.
         */
        public void SetTerms(TradeSide side, string gives, int givesAmount, string gets, int getsAmount)
        {
            givesAmount = Math.Max(1, givesAmount);
            getsAmount = Math.Max(1, getsAmount);
            if (side == TradeSide.A)
            {
                aGood = gives; aAmount = givesAmount; bGood = gets; bAmount = getsAmount;
            }
            else
            {
                bGood = gives; bAmount = givesAmount; aGood = gets; aAmount = getsAmount;
            }
            SetApproved(side, true);
            SetApproved(Other(side), false);
        }

        public static TradeSide Other(TradeSide side) => side == TradeSide.A ? TradeSide.B : TradeSide.A;

        public string Serialize()
        {
            return string.Join(Separator.ToString(), id, aGood ?? "", aAmount, bGood ?? "", bAmount, aApproved, bApproved);
        }

        public static TradeDeal Deserialize(string value)
        {
            string[] parts = value.Split(Separator);
            if (parts.Length != 7 || !int.TryParse(parts[0], out int id)) return null;
            return new TradeDeal()
            {
                id = id,
                aGood = parts[1] == "" ? null : parts[1],
                aAmount = int.TryParse(parts[2], out int a) ? Math.Max(1, a) : DefaultAmount,
                bGood = parts[3] == "" ? null : parts[3],
                bAmount = int.TryParse(parts[4], out int b) ? Math.Max(1, b) : DefaultAmount,
                aApproved = parts[5] == bool.TrueString,
                bApproved = parts[6] == bool.TrueString,
            };
        }
    }

    /**
     * A trading post is a district crossing that also swaps goods between
     * the districts on its two sides. Both halves share one list of deals,
     * stored on side A (the half with the lower entity ID, so every
     * machine agrees). A deal runs as soon as both sides have approved
     * it, whenever both districts have the goods and room for what they
     * receive.
     *
     * Goods move straight between stockpiles, picked in entity ID order,
     * so every co-op player gets the same result.
     */
    public class DistrictTrade : TickableComponent, IAwakableComponent, IPersistentEntity, IFinishedStateListener, IInitializableEntity
    {
        private static readonly ComponentKey DistrictTradeKey = new ComponentKey("BeaverBuddies.DistrictTrade");
        private static readonly ListKey<string> DealsKey = new ListKey<string>("Deals");
        private static readonly PropertyKey<int> NextDealIdKey = new PropertyKey<int>("NextDealId");
        // Trades from before shared deals, converted on the first tick
        private static readonly ListKey<string> LegacyRulesKey = new ListKey<string>("Rules");
        private static readonly PropertyKey<string> LegacyGiveGoodKey = new PropertyKey<string>("GiveGood");
        private static readonly PropertyKey<int> LegacyGiveAmountKey = new PropertyKey<int>("GiveAmount");
        private static readonly PropertyKey<string> LegacyGetGoodKey = new PropertyKey<string>("GetGood");
        private static readonly PropertyKey<int> LegacyGetAmountKey = new PropertyKey<int>("GetAmount");

        public const int MaxDeals = 10;
        // How much of each good a half holds
        public const int Capacity = 50;
        // Trades' worth of goods the workers keep in their half
        private const int StockedTrades = 2;

        private readonly IDayNightCycle _dayNightCycle;

        private DistrictBuilding _districtBuilding;
        private LinkedBuilding _linkedBuilding;

        // The goods this half gives and has received, moved by its workers
        public Inventory Inventory { get; private set; }
        private int _nextDealId = 1;
        private readonly List<TradeDeal> _deals = new();
        private readonly List<string> _legacyRules = new();
        // Only for the UI, recomputed every hour on every machine
        private readonly Dictionary<int, TradeStatus> _statuses = new();

        public DistrictTrade(IDayNightCycle dayNightCycle)
        {
            _dayNightCycle = dayNightCycle;
        }

        public void Awake()
        {
            _districtBuilding = GetComponent<DistrictBuilding>();
            _linkedBuilding = GetComponent<LinkedBuilding>();
            DisableComponent();
        }

        public void InitializeInventory(Inventory inventory)
        {
            Inventory = inventory;
        }

        // Shown on a half with deals its side hasn't approved yet: an icon
        // over it and an alert in the corner (only for its own district)
        private Timberborn.StatusSystem.StatusToggle _pendingStatus;

        public void InitializeEntity()
        {
            var subject = GetComponent<Timberborn.StatusSystem.StatusSubject>();
            if (!subject) return;
            _pendingStatus = Timberborn.StatusSystem.StatusToggle.CreateNormalStatusWithAlertAndFloatingIcon("TradeDealWarning",
                RegisteredLocalizationService.T("BeaverBuddies.Trading.Problem.Pending"),
                RegisteredLocalizationService.T("BeaverBuddies.Trading.Problem.Pending.Short"));
            subject.RegisterStatus(_pendingStatus);
        }

        private void UpdatePendingStatus()
        {
            if (_pendingStatus == null) return;
            TradeSide side = Side;
            _pendingStatus.Toggle(Linked != null && Deals.Any(d => d.IsConfigured && !d.Approved(side)));
        }

        public void OnEnterFinishedState()
        {
            EnableComponent();
            Inventory?.Enable();
        }

        public void OnExitFinishedState()
        {
            DisableComponent();
            Inventory?.Disable();
            _pendingStatus?.Deactivate();
        }

        /**
         * How much of a good this half's workers keep in it: a couple of
         * trades' worth of what this side gives in its active deals. The
         * rest (what it received) they carry to their stockpiles.
         */
        public int WantedStock(string good)
        {
            TradeSide side = Side;
            int wanted = Deals.Where(d => d.IsConfigured && d.IsApproved && d.Gives(side) == good)
                .Sum(d => d.GivesAmount(side) * StockedTrades);
            return Math.Min(Capacity, wanted);
        }

        public void Save(IEntitySaver entitySaver)
        {
            IObjectSaver saver = entitySaver.GetComponent(DistrictTradeKey);
            saver.Set(DealsKey, _deals.Select(d => d.Serialize()).ToList());
            saver.Set(NextDealIdKey, _nextDealId);
            if (_legacyRules.Count > 0) saver.Set(LegacyRulesKey, _legacyRules);
        }

        public void Load(IEntityLoader entityLoader)
        {
            if (!entityLoader.TryGetComponent(DistrictTradeKey, out IObjectLoader loader)) return;
            if (loader.Has(NextDealIdKey)) _nextDealId = loader.Get(NextDealIdKey);
            if (loader.Has(DealsKey))
            {
                foreach (string value in loader.Get(DealsKey))
                {
                    TradeDeal deal = TradeDeal.Deserialize(value);
                    if (deal != null) _deals.Add(deal);
                }
            }
            if (loader.Has(LegacyRulesKey)) _legacyRules.AddRange(loader.Get(LegacyRulesKey));
            if (loader.Has(LegacyGiveGoodKey) || loader.Has(LegacyGetGoodKey))
            {
                _legacyRules.Add(string.Join("\t",
                    loader.Has(LegacyGiveGoodKey) ? loader.Get(LegacyGiveGoodKey) : "",
                    loader.Has(LegacyGiveAmountKey) ? loader.Get(LegacyGiveAmountKey) : TradeDeal.DefaultAmount,
                    loader.Has(LegacyGetGoodKey) ? loader.Get(LegacyGetGoodKey) : "",
                    loader.Has(LegacyGetAmountKey) ? loader.Get(LegacyGetAmountKey) : TradeDeal.DefaultAmount));
            }
        }

        // ---- The two halves ----

        /**
         * The other half of this trading post, if it's a trading post too.
         */
        public DistrictTrade Linked => _linkedBuilding.IsLinked
            ? _linkedBuilding._linked.GetComponent<DistrictTrade>()
            : null;

        public TradeSide Side
        {
            get
            {
                DistrictTrade linked = Linked;
                if (linked == null) return TradeSide.A;
                return GetComponent<EntityComponent>().EntityId.CompareTo(linked.GetComponent<EntityComponent>().EntityId) < 0
                    ? TradeSide.A
                    : TradeSide.B;
            }
        }

        /**
         * The half that stores the deals.
         */
        public DistrictTrade SideA => Side == TradeSide.A ? this : Linked;

        public DistrictTrade GetHalf(TradeSide side) => Side == side ? this : Linked;

        public IReadOnlyList<TradeDeal> Deals => SideA?._deals ?? (IReadOnlyList<TradeDeal>)Array.Empty<TradeDeal>();

        public DistrictCenter District => _districtBuilding.District;

        /**
         * The owner of the district on this side, or null if it has none.
         */
        public string Owner => District == null
            ? null
            : DistrictOwners.Of(District);

        /**
         * Only the owner of a side's district can act for that side.
         * Districts without an owner are open to everyone.
         */
        public bool CanEdit(string playerID)
        {
            string owner = Owner;
            return owner == null || owner == playerID;
        }

        public TradeStatus GetStatus(TradeDeal deal)
        {
            if (!deal.IsConfigured) return TradeStatus.NotConfigured;
            if (!deal.IsApproved) return TradeStatus.AwaitingApproval;
            DistrictTrade sideA = SideA;
            return sideA != null && sideA._statuses.TryGetValue(deal.id, out TradeStatus status)
                ? status
                : TradeStatus.NotConnected;
        }

        // ---- Changes (from replayed events, checked by DistrictDealEvent) ----

        public TradeDeal FindDeal(int id) => SideA?._deals.FirstOrDefault(d => d.id == id);

        // The new deal, or null if there's no room for it
        public TradeDeal AddDeal(TradeSide side, string gives, int givesAmount, string gets, int getsAmount)
        {
            DistrictTrade sideA = SideA;
            if (sideA == null || sideA._deals.Count >= MaxDeals) return null;
            var deal = new TradeDeal() { id = sideA._nextDealId++ };
            deal.SetTerms(side, gives, givesAmount, gets, getsAmount);
            sideA._deals.Add(deal);
            return deal;
        }

        public void RemoveDeal(int id)
        {
            DistrictTrade sideA = SideA;
            sideA?._deals.RemoveAll(d => d.id == id);
            sideA?._statuses.Remove(id);
        }

        // ---- Trading ----

        public override void Tick()
        {
            // Trades as soon as both halves have the goods and the room
            ConvertLegacyRules();
            UpdatePendingStatus();
            if (Side != TradeSide.A) return;

            _statuses.Clear();
            foreach (TradeDeal deal in _deals)
            {
                if (deal.IsConfigured && deal.IsApproved) _statuses[deal.id] = TryTrade(deal);
            }
        }

        /**
         * Before shared deals each half had its own list of trades. Turn
         * them into deals approved by the side that had them; a trade both
         * sides had becomes a single deal approved by both.
         */
        private void ConvertLegacyRules()
        {
            if (_legacyRules.Count == 0) return;
            DistrictTrade sideA = SideA;
            if (sideA == null) return;
            TradeSide side = Side;
            foreach (string value in _legacyRules)
            {
                string[] parts = value.Split('\t');
                if (parts.Length != 4) continue;
                string gives = parts[0] == "" ? null : parts[0];
                string gets = parts[2] == "" ? null : parts[2];
                int givesAmount = int.TryParse(parts[1], out int g) ? g : TradeDeal.DefaultAmount;
                int getsAmount = int.TryParse(parts[3], out int r) ? r : TradeDeal.DefaultAmount;
                TradeDeal same = sideA._deals.FirstOrDefault(d => !d.Approved(side)
                    && d.Gives(side) == gives && d.GivesAmount(side) == givesAmount
                    && d.Gets(side) == gets && d.GetsAmount(side) == getsAmount);
                if (same != null) same.SetApproved(side, true);
                else AddDeal(side, gives, givesAmount, gets, getsAmount);
            }
            _legacyRules.Clear();
        }

        /**
         * Swaps the goods between the two halves' storage. The workers of
         * each half bring what it gives and take away what it received.
         */
        private TradeStatus TryTrade(TradeDeal deal)
        {
            DistrictTrade there = Linked;
            if (District == null || there == null || there.District == null || District == there.District) return TradeStatus.NotConnected;
            Inventory here = Inventory;
            Inventory thereInventory = there.Inventory;
            if (here == null || thereInventory == null || !here.Enabled || !thereInventory.Enabled) return TradeStatus.NotConnected;

            // This is side A: it gives aGood and gets bGood
            if (here.UnreservedAmountInStock(deal.aGood) < deal.aAmount) return TradeStatus.MissingGoodsHere;
            if (thereInventory.UnreservedAmountInStock(deal.bGood) < deal.bAmount) return TradeStatus.MissingGoodsThere;
            if (here.UnreservedCapacity(deal.bGood) < deal.bAmount) return TradeStatus.NoSpaceHere;
            if (thereInventory.UnreservedCapacity(deal.aGood) < deal.aAmount) return TradeStatus.NoSpaceThere;

            here.TakeExisting(new GoodAmount(deal.aGood, deal.aAmount));
            thereInventory.TakeExisting(new GoodAmount(deal.bGood, deal.bAmount));
            thereInventory.GiveExisting(new GoodAmount(deal.aGood, deal.aAmount));
            here.GiveExisting(new GoodAmount(deal.bGood, deal.bAmount));
            return TradeStatus.Traded;
        }
    }
}

namespace BeaverBuddies.Trading
{
    /**
     * Each half's storage: any good, a limited amount of each, like the
     * Power Exchange's.
     */
    public class DistrictTradeInventoryInitializer : Timberborn.TemplateInstantiation.IDedicatedDecoratorInitializer<DistrictTrade, Inventory>
    {
        public const string InventoryComponentName = "BeaverBuddies.TradingPost";

        private readonly IGoodService _goodService;
        private readonly Timberborn.InventoryNeedSystem.InventoryNeedBehaviorInitializer _inventoryNeedBehaviorInitializer;
        private readonly InventoryInitializerFactory _inventoryInitializerFactory;

        public DistrictTradeInventoryInitializer(IGoodService goodService,
            Timberborn.InventoryNeedSystem.InventoryNeedBehaviorInitializer inventoryNeedBehaviorInitializer,
            InventoryInitializerFactory inventoryInitializerFactory)
        {
            _goodService = goodService;
            _inventoryNeedBehaviorInitializer = inventoryNeedBehaviorInitializer;
            _inventoryInitializerFactory = inventoryInitializerFactory;
        }

        public void Initialize(DistrictTrade subject, Inventory decorator)
        {
            InventoryInitializer initializer = _inventoryInitializerFactory.CreateWithUnlimitedCapacity(decorator, InventoryComponentName);
            foreach (string good in _goodService.Goods)
            {
                initializer.AddAllowedGood(new StorableGoodAmount(StorableGood.CreateAsTakeable(good), DistrictTrade.Capacity));
            }
            initializer.Initialize();
            subject.InitializeInventory(decorator);
            _inventoryNeedBehaviorInitializer.AddNeedBehavior(decorator);
        }
    }

    /**
     * What a half's workers do: carry away what it received (anything it
     * doesn't need to give), then bring what its side gives.
     */
    public class DistrictTradeWorkplaceBehavior : Timberborn.WorkSystem.WorkplaceBehavior, IAwakableComponent
    {
        private DistrictTrade _trade;

        public void Awake()
        {
            _trade = GetComponent<DistrictTrade>();
        }

        public override Timberborn.BehaviorSystem.Decision Decide(Timberborn.BehaviorSystem.BehaviorAgent agent)
        {
            Inventory inventory = _trade.Inventory;
            if (inventory == null || !inventory.Enabled) return Timberborn.BehaviorSystem.Decision.ReleaseNow();

            var starter = agent.GetComponent<Timberborn.Emptying.EmptyingStarter>();
            foreach (GoodAmount stock in inventory.UnreservedStock())
            {
                int excess = stock.Amount - _trade.WantedStock(stock.GoodId);
                if (excess <= 0) continue;
                var (amount, target) = starter.GetCarriableGood(inventory, new[] { new GoodAmount(stock.GoodId, excess) });
                if (amount.Amount <= 0) continue;
                var reserver = agent.GetComponent<Timberborn.InventorySystem.GoodReserver>();
                reserver.ReserveExactStockAmount(inventory, amount);
                reserver.ReserveCapacity(target, amount);
                return Timberborn.BehaviorSystem.Decision.ReleaseNextTick();
            }

            var carrier = agent.GetComponent<Timberborn.Carrying.CarrierInventoryFinder>();
            var goods = new HashSet<string>();
            TradeSide side = _trade.Side;
            foreach (TradeDeal deal in _trade.Deals)
            {
                if (!deal.IsConfigured || !deal.IsApproved || !goods.Add(deal.Gives(side))) continue;
                string good = deal.Gives(side);
                int have = inventory.AmountInStock(good) + inventory.ReservedCapacity(good);
                int missing = _trade.WantedStock(good) - have;
                if (missing > 0 && carrier.TryCarryFromAnyInventoryLimited(good, inventory, missing))
                {
                    return Timberborn.BehaviorSystem.Decision.ReleaseNextTick();
                }
            }
            return Timberborn.BehaviorSystem.Decision.ReleaseNow();
        }
    }
}

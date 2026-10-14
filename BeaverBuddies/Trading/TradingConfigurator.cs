using Bindito.Core;
using Timberborn.BatchControl;
using Timberborn.EntityPanelSystem;
using Timberborn.TemplateInstantiation;

using BeaverBuddies.Events;
using BeaverBuddies.Modding;
using BeaverBuddies.Players;
using BeaverBuddies.Util;

namespace BeaverBuddies.Trading
{
    public static class TradingConfigurator
    {
        private class TemplateModuleProvider : IProvider<TemplateModule>
        {
            private readonly DistrictTradeInventoryInitializer _inventoryInitializer;

            public TemplateModuleProvider(DistrictTradeInventoryInitializer inventoryInitializer)
            {
                _inventoryInitializer = inventoryInitializer;
            }

            // Workers set up like the Power Exchange's
            public TemplateModule Get()
            {
                TemplateModule.Builder builder = new TemplateModule.Builder();
                builder.AddDedicatedDecorator(_inventoryInitializer);
                builder.AddDecorator<DistrictTradeSpec, DistrictTrade>();
                builder.AddDecorator<DistrictTrade, DistrictTradeWorkplaceBehavior>();
                builder.AddDecorator<DistrictTrade, Timberborn.LaborSystem.LaborWorkplaceBehavior>();
                builder.AddDecorator<DistrictTrade, Timberborn.WorkSystem.WaitInsideIdlyWorkplaceBehavior>();
                builder.AddDecorator<DistrictTrade, Timberborn.Hauling.WorkplaceWithBackpacks>();
                builder.AddDecorator<DistrictTrade, Timberborn.InventoryNeedSystem.InventoryNeedBehavior>();
                return builder.Build();
            }
        }

        private class EntityPanelModuleProvider : IProvider<EntityPanelModule>
        {
            private readonly DistrictTradeFragment _districtTradeFragment;
            private readonly TradingPostInventoryFragment _inventoryFragment;

            public EntityPanelModuleProvider(DistrictTradeFragment districtTradeFragment, TradingPostInventoryFragment inventoryFragment)
            {
                _districtTradeFragment = districtTradeFragment;
                _inventoryFragment = inventoryFragment;
            }

            public EntityPanelModule Get()
            {
                EntityPanelModule.Builder builder = new EntityPanelModule.Builder();
                builder.AddMiddleFragment(_districtTradeFragment);
                builder.AddBottomFragment(_inventoryFragment);
                return builder.Build();
            }
        }

        private class BatchControlModuleProvider : IProvider<BatchControlModule>
        {
            private readonly TradesBatchControlTab _tradesTab;

            public BatchControlModuleProvider(TradesBatchControlTab tradesTab)
            {
                _tradesTab = tradesTab;
            }

            public BatchControlModule Get()
            {
                BatchControlModule.Builder builder = new BatchControlModule.Builder();
                builder.AddTab(_tradesTab, TradesBatchControlTab.Order);
                return builder.Build();
            }
        }

        public static void Configure(IContainerDefinition containerDefinition)
        {
            containerDefinition.Bind<DistrictTrade>().AsTransient();
            containerDefinition.Bind<DistrictTradeWorkplaceBehavior>().AsTransient();
            containerDefinition.Bind<DistrictTradeInventoryInitializer>().AsSingleton();
            containerDefinition.Bind<TradingPostInventoryFragment>().AsSingleton();
            containerDefinition.Bind<DistrictTradeFragment>().AsSingleton();
            containerDefinition.Bind<TradesUi>().AsSingleton();
            containerDefinition.Bind<TradesBatchControlTab>().AsSingleton();
            containerDefinition.Bind<TradesTabOpener>().AsSingleton();
            containerDefinition.MultiBind<BatchControlModule>().ToProvider<BatchControlModuleProvider>().AsSingleton();
            containerDefinition.MultiBind<TemplateModule>().ToProvider<TemplateModuleProvider>().AsSingleton();
            containerDefinition.MultiBind<EntityPanelModule>().ToProvider<EntityPanelModuleProvider>().AsSingleton();
        }
    }
}

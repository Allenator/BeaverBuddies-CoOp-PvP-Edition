// Copied from Archipelago/Archipelago/Trade/Shared by Tools/sync_trade.py: edit it there and run the script.
using System;
using System.Collections.Generic;
using System.Linq;
using Timberborn.BaseComponentSystem;
using Timberborn.BatchControl;
using Timberborn.CoreUI;
using Timberborn.DropdownSystem;
using Timberborn.EntityPanelSystem;
using Timberborn.Goods;
using Timberborn.GoodsUI;
using Timberborn.SelectionSystem;
using Timberborn.TooltipSystem;
using UnityEngine;
using UnityEngine.UIElements;

namespace BeaverBuddies.Trading
{
    /** Small pieces of the game's own UI the trade panels are made of. */
    internal static class Pieces
    {
        private const string PanelPath = "Game/EntityPanel/PopulationCounterFragment";

        public static Label Label(string text = "", int size = 0)
        {
            var label = new Label(text);
            label.AddToClassList("entity-panel__text");
            label.AddToClassList("game-text-normal");
            if (size > 0) label.style.fontSize = size;
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        public static NineSliceButton Button(string text, Action onClick, bool red = false)
        {
            var button = new NineSliceButton();
            button.AddToClassList("entity-panel__text");
            button.AddToClassList("entity-fragment__button");
            // The plain class has no look of its own outside the entity panel's middle: green or red, as the game's
            button.AddToClassList(red ? "entity-fragment__button--red" : "entity-fragment__button--green");
            button.text = text;
            // The game's class stretches a button across its row: these keep to their text unless told otherwise
            button.style.flexGrow = 0;
            button.style.flexShrink = 0;
            button.style.width = button.style.minWidth = StyleKeyword.Auto;
            button.style.marginLeft = button.style.marginRight = 2;
            button.style.marginTop = button.style.marginBottom = 0;
            button.style.paddingLeft = button.style.paddingRight = 6;
            button.RegisterCallback<ClickEvent>(_ => onClick());
            return button;
        }

        /** A box like the game's sub-panels, edged in gold for what's happening now. */
        public static VisualElement Box(bool current = false)
        {
            var box = new NineSliceVisualElement();
            box.AddToClassList("bg-sub-box--green");
            Highlight(box, current);
            box.style.paddingLeft = box.style.paddingRight = 6;
            box.style.paddingTop = box.style.paddingBottom = 4;
            box.style.marginBottom = 4;
            return box;
        }

        public static void Highlight(VisualElement box, bool on)
        {
            box.style.borderTopWidth = box.style.borderBottomWidth = box.style.borderLeftWidth = box.style.borderRightWidth = on ? 1 : 0;
            box.style.borderTopColor = box.style.borderBottomColor = box.style.borderLeftColor = box.style.borderRightColor = new Color(0.85f, 0.72f, 0.36f);
        }

        /** A thin gold line between sections. */
        public static VisualElement Separator()
        {
            var line = new VisualElement();
            line.style.height = 1;
            line.style.marginTop = line.style.marginBottom = 6;
            line.style.backgroundColor = new Color(0.8f, 0.68f, 0.35f, 0.35f);
            return line;
        }

        /** A section's title, in the game's yellow. */
        public static Label Title(string text)
        {
            var label = Label(text, 13);
            label.AddToClassList("text--yellow");
            label.style.marginBottom = 3;
            return label;
        }

        /** Goods as icons with their amounts, wrapping. */
        public static VisualElement Goods(IEnumerable<KeyValuePair<string, int>> goods, GoodDescriber describer, ITooltipRegistrar tooltips)
        {
            var row = Row();
            row.style.flexWrap = Wrap.Wrap;
            foreach (var good in goods)
            {
                var described = describer.GetDescribedGood(good.Key);
                var item = Row();
                item.style.flexShrink = 0;
                item.style.marginRight = 8;
                item.Add(Icon(described.Icon, 20));
                var amount = Label(good.Value.ToString(), 12);
                amount.style.whiteSpace = WhiteSpace.NoWrap;
                amount.style.marginLeft = 2;
                item.Add(amount);
                tooltips.Register(item, () => described.DisplayName);
                row.Add(item);
            }
            return row;
        }

        /** Holds a button to this width, whatever the game's style asks for. */
        public static NineSliceButton Sized(NineSliceButton button, int width, int height = 24)
        {
            button.style.width = button.style.minWidth = button.style.maxWidth = width;
            button.style.height = button.style.minHeight = height;
            button.style.paddingLeft = button.style.paddingRight = 0;
            return button;
        }

        public static Image Icon(Sprite sprite, int size = 22)
        {
            var icon = new Image { sprite = sprite, scaleMode = ScaleMode.ScaleToFit };
            icon.style.width = icon.style.height = size;
            icon.style.flexShrink = 0;
            return icon;
        }

        public static VisualElement Row()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            return row;
        }

        public static IntegerField Number(VisualElementLoader loader, int width)
        {
            var field = loader.LoadVisualElement(PanelPath).Q<IntegerField>("Threshold");
            field.RemoveFromHierarchy();
            field.isDelayed = true;
            field.style.width = width;
            field.style.flexShrink = 0;
            field.style.marginLeft = 4;
            field.style.marginRight = field.style.marginTop = field.style.marginBottom = 0;
            // Framed as the game's number boxes, wherever it goes
            VisualElement input = field.Q(className: "unity-base-field__input") ?? field;
            input.style.backgroundColor = new Color(0.08f, 0.12f, 0.1f, 0.9f);
            input.style.borderTopWidth = input.style.borderBottomWidth = input.style.borderLeftWidth = input.style.borderRightWidth = 1;
            input.style.borderTopColor = input.style.borderBottomColor = input.style.borderLeftColor = input.style.borderRightColor = new Color(0.8f, 0.68f, 0.35f, 0.6f);
            input.style.paddingLeft = input.style.paddingRight = 3;
            return field;
        }

        /** A number box like Number's, taking fractions. */
        public static FloatField Decimal(VisualElementLoader loader, int width)
        {
            IntegerField like = Number(loader, width);
            var field = new FloatField { isDelayed = true };
            foreach (string c in like.GetClasses()) field.AddToClassList(c);
            field.style.width = width;
            field.style.flexShrink = 0;
            field.style.marginLeft = 4;
            field.style.marginRight = field.style.marginTop = field.style.marginBottom = 0;
            VisualElement input = field.Q(className: "unity-base-field__input") ?? field;
            input.style.backgroundColor = new Color(0.08f, 0.12f, 0.1f, 0.9f);
            input.style.borderTopWidth = input.style.borderBottomWidth = input.style.borderLeftWidth = input.style.borderRightWidth = 1;
            input.style.borderTopColor = input.style.borderBottomColor = input.style.borderLeftColor = input.style.borderRightColor = new Color(0.8f, 0.68f, 0.35f, 0.6f);
            input.style.paddingLeft = input.style.paddingRight = 3;
            return field;
        }

        public static Dropdown Dropdown(VisualElementLoader loader)
        {
            Dropdown dropdown = loader.LoadVisualElement(PanelPath).Q<Dropdown>("Mode");
            dropdown.RemoveFromHierarchy();
            dropdown.style.marginLeft = dropdown.style.marginRight = 0;
            return dropdown;
        }
    }

    // ---- The post's own panel ----

    /**
     * A trade post's panel: what it sells and buys and at what value, like the District Crossing's imported goods, and
     * a button that opens the Trade tab on it, like the Crossing's to the Distribution tab.
     */
    public class TradeSiteFragment : IEntityPanelFragment
    {
        private readonly VisualElementLoader _visualElementLoader;
        private readonly GoodDescriber _goodDescriber;
        private readonly ITooltipRegistrar _tooltips;
        private readonly IGoodService _goodService;
        private VisualElement _root;
        private VisualElement _content;
        private Label _title;
        private Button _button;
        private ITradeSite _site;
        private BaseComponent _entity;
        private int _shown = -1;

        // The post of an entity, if it's one of this mod's
        public static Func<BaseComponent, ITradeSite> SiteOf;

        public TradeSiteFragment(VisualElementLoader visualElementLoader, GoodDescriber goodDescriber, ITooltipRegistrar tooltips, IGoodService goodService)
        {
            _visualElementLoader = visualElementLoader;
            _goodDescriber = goodDescriber;
            _tooltips = tooltips;
            _goodService = goodService;
        }

        public VisualElement InitializeFragment()
        {
            _root = _visualElementLoader.LoadVisualElement("Game/EntityPanel/DistrictCrossingFragment");
            string imported = TradeText.T("Distribution.ImportedGoods");
            _root.Query<Label>().ForEach(l => { if (l.text == imported) _title = l; });
            _content = _root.Q<VisualElement>("ImportGoodsWrapper");
            _content.Clear();
            _content.style.flexDirection = FlexDirection.Column;
            _content.style.alignItems = Align.Stretch;
            _content.style.paddingLeft = _content.style.paddingRight = 8;
            _content.style.paddingBottom = 6;
            // The Crossing's own button to the Distribution tab, styled as the game's, leads to the Trade tab
            _button = _root.Q<Button>("DistributionButton");
            _button.name = "TradeSettings";
            _button.RegisterCallback<ClickEvent>(evt =>
            {
                evt.StopImmediatePropagation();
                TradeHub.Open(_entity);
            }, TrickleDown.TrickleDown);
            _root.ToggleDisplayStyle(false);
            return _root;
        }

        public void ShowFragment(BaseComponent entity)
        {
            _site = SiteOf?.Invoke(entity);
            _entity = _site?.Entity;
            _shown = -1;
            _root.ToggleDisplayStyle(_site != null);
        }

        public void ClearFragment()
        {
            _site = null;
            _entity = null;
            _root.ToggleDisplayStyle(false);
        }

        public void UpdateFragment()
        {
            if (_site == null || !_entity || _site.Version == _shown) return;
            _shown = _site.Version;
            _title.text = _site.Title;
            _content.Clear();
            var rules = Rules().ToList();
            Line(rules, TradeMode.Sell, "BeaverBuddies.Trade.Sells");
            Line(rules, TradeMode.Buy, "BeaverBuddies.Trade.Buys");
            if (rules.Count == 0) _content.Add(Pieces.Label(TradeText.T("BeaverBuddies.Trade.NothingYet")));
            _button.text = TradeText.T(_site.Editable ? "BeaverBuddies.Trade.SetTrades" : "BeaverBuddies.Trade.SeeTrades");
        }

        private IEnumerable<KeyValuePair<string, TradeRule>> Rules()
        {
            foreach (string good in _goodService.Goods)
            {
                TradeRule rule = _site.Rule(good);
                if (rule.Mode != TradeMode.Off) yield return new(good, rule);
            }
        }

        // One line of goods, each its icon with its value under
        private void Line(List<KeyValuePair<string, TradeRule>> rules, TradeMode mode, string titleKey)
        {
            var goods = rules.Where(r => r.Value.Mode == mode).ToList();
            if (goods.Count == 0) return;
            _content.Add(Pieces.Title(TradeText.T(titleKey)));
            var row = Pieces.Row();
            row.style.flexWrap = Wrap.Wrap;
            foreach (var good in goods)
            {
                var item = new VisualElement();
                item.style.alignItems = Align.Center;
                item.style.marginRight = 6;
                item.style.marginBottom = 2;
                var described = _goodDescriber.GetDescribedGood(good.Key);
                item.Add(Pieces.Icon(described.Icon, 26));
                item.Add(Pieces.Label(good.Value.Value.ToString(), 12));
                _tooltips.Register(item, () => TradeText.T("BeaverBuddies.Trade.ValueTooltip", described.DisplayName, good.Value.Value));
                row.Add(item);
            }
            _content.Add(row);
        }
    }

    // ---- The Trade tab ----

    /**
     * The trade posts' part of the Trade tab: a group for each post with a card for each good, whether the post buys it,
     * sells it or leaves it, what one is worth and how far it goes.
     */
    public class TradeGroups
    {
        private const int CardWidth = 196;

        private readonly BatchControlRowGroupFactory _rowGroupFactory;
        private readonly GoodsGroupSpecService _groups;
        private readonly IGoodService _goodService;
        private readonly GoodDescriber _goodDescriber;
        private readonly ITooltipRegistrar _tooltips;
        private readonly VisualElementLoader _loader;
        private readonly EntitySelectionService _selection;
        private readonly Timberborn.SliderToggleSystem.SliderToggleFactory _sliderToggles;
        private readonly Timberborn.AssetSystem.IAssetLoader _assets;
        private List<StyleSheet> _sheets;
        // Which of a post's goods the multiply row changes: 0 all, 1 those it sells, 2 those it buys
        private readonly Dictionary<Guid, int> _scopes = new();

        public TradeGroups(BatchControlRowGroupFactory rowGroupFactory, GoodsGroupSpecService groups, IGoodService goodService,
            GoodDescriber goodDescriber, ITooltipRegistrar tooltips, VisualElementLoader loader, EntitySelectionService selection,
            Timberborn.SliderToggleSystem.SliderToggleFactory sliderToggles, Timberborn.AssetSystem.IAssetLoader assets)
        {
            _sliderToggles = sliderToggles;
            _assets = assets;
            _rowGroupFactory = rowGroupFactory;
            _groups = groups;
            _goodService = goodService;
            _goodDescriber = goodDescriber;
            _tooltips = tooltips;
            _loader = loader;
            _selection = selection;
        }

        /** The posts to show: the ones this player sets, and the one opened from its panel first, whoever's it is. */
        public static List<ITradeSite> Pick(IEnumerable<ITradeSite> sites, Guid focus)
        {
            var all = sites.Where(s => s != null && s.Entity).ToList();
            var shown = all.Where(s => s.Editable).OrderBy(s => s.SortKey, StringComparer.Ordinal).ThenBy(s => s.Id).ToList();
            ITradeSite first = all.FirstOrDefault(s => s.Id == focus);
            if (first != null)
            {
                shown.RemoveAll(s => s.Id == focus);
                shown.Insert(0, first);
            }
            return shown;
        }

        // The entity panel's styles, for its buttons, frames and number fields, which the settlement panel doesn't load
        private void Styled(VisualElement element)
        {
            if (_sheets == null)
            {
                _sheets = new List<StyleSheet>();
                foreach (string path in new[] { "Game/EntityPanel/DistrictCrossingFragment", "Game/EntityPanel/PopulationCounterFragment" })
                {
                    VisualElement loaded = _loader.LoadVisualElement(path);
                    loaded.Query<VisualElement>().ForEach(e =>
                    {
                        for (int i = 0; i < e.styleSheets.count; i++) if (!_sheets.Contains(e.styleSheets[i])) _sheets.Add(e.styleSheets[i]);
                    });
                }
            }
            foreach (StyleSheet sheet in _sheets) element.styleSheets.Add(sheet);
        }

        // Redrawn when a post comes or goes, or is finished
        public static string LayoutKey(IEnumerable<ITradeSite> sites) => string.Join(",", sites.Select(s => s.Id + (s.Finished ? "" : "~")));

        public VisualElement Header(string text)
        {
            VisualElement header = _loader.LoadVisualElement("Game/BatchControl/BatchControlHeaderRow");
            header.Q<Label>("Text").text = text;
            return header;
        }

        /** The group shown when there are no posts at all. */
        public BatchControlRowGroup Empty(string title, string text)
        {
            var empty = _rowGroupFactory.CreateUnsorted(new BatchControlRow(Header(title)));
            var row = Pieces.Row();
            row.style.paddingLeft = row.style.paddingTop = row.style.paddingBottom = 8;
            row.Add(Pieces.Label(text));
            empty.AddRow(new BatchControlRow(row));
            return empty;
        }

        public BatchControlRowGroup Group(ITradeSite site)
        {
            BaseComponent entity = site.Entity;
            VisualElement header = Header(site.Title + (site.Finished ? "" : " - " + TradeText.T("BeaverBuddies.Trade.Unfinished")));
            // The header picks the post out on the map
            header.RegisterCallback<ClickEvent>(_ => { if (entity) _selection.SelectAndFocusOn(entity); });
            var group = _rowGroupFactory.CreateUnsorted(new BatchControlRow(header));
            var help = Pieces.Row();
            help.style.paddingLeft = help.style.paddingRight = 8;
            help.style.paddingTop = 4;
            help.Add(Pieces.Label(site.Editable ? site.Help : TradeText.T("BeaverBuddies.Trade.TheirsHelp")));
            group.AddRow(new BatchControlRow(help));
            if (site.Editable)
            {
                VisualElement multiplier = Multiplier(site, out Timberborn.SliderToggleSystem.SliderToggle toggle);
                group.AddRow(new BatchControlRow(multiplier, new Updater(toggle.Update)));
            }
            var cards = new List<Card>();
            foreach (GoodGroupSpec goodGroup in _groups.GoodGroupSpecs.OrderBy(g => g.Order))
            {
                var goods = _goodService.GetGoodsForGroup(goodGroup.Id).Where(site.Takes).ToList();
                if (goods.Count == 0) continue;
                var row = Pieces.Row();
                Styled(row);
                row.style.alignItems = Align.FlexStart;
                row.style.flexShrink = 0;
                row.style.height = StyleKeyword.Auto;
                row.style.paddingLeft = row.style.paddingRight = 6;
                row.style.paddingTop = row.style.paddingBottom = 4;
                var groupIcon = Pieces.Icon(goodGroup.Icon.Asset, 32);
                groupIcon.style.marginTop = 6;
                groupIcon.style.marginRight = 6;
                row.Add(groupIcon);
                var items = Pieces.Row();
                items.style.flexWrap = Wrap.Wrap;
                items.style.flexGrow = 1;
                items.style.flexShrink = 1;
                row.Add(items);
                foreach (string good in goods)
                {
                    var card = new Card(this, site, good);
                    items.Add(card.Root);
                    cards.Add(card);
                }
                group.AddRow(new BatchControlRow(row, new Updater(() => cards.ForEach(c => c.Update()))));
            }
            return group;
        }

        /**
         * A row that multiplies the values a post has set, so its prices move together: a factor typed in, applied on
         * Enter, and the game's slider toggle for which goods, all of them, those it sells (green arrow) or those it
         * buys (red arrow).
         */
        private VisualElement Multiplier(ITradeSite site, out Timberborn.SliderToggleSystem.SliderToggle toggle)
        {
            var row = Pieces.Row();
            Styled(row);
            row.style.paddingLeft = row.style.paddingRight = 8;
            row.style.paddingTop = row.style.paddingBottom = 4;
            var label = Pieces.Label(TradeText.T("BeaverBuddies.Trade.Multiply"), 12);
            label.style.marginRight = 6;
            row.Add(label);
            _tooltips.Register(label, () => TradeText.T("BeaverBuddies.Trade.Multiply.Tooltip"));
            FloatField factor = Pieces.Decimal(_loader, 52);
            factor.name = "TradeMultiplyFactor";
            factor.SetValueWithoutNotify(1f);
            factor.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue > 0 && !Mathf.Approximately(evt.newValue, 1f)) Multiply(site, evt.newValue);
                // Back to 1, ready for the next
                factor.SetValueWithoutNotify(1f);
            });
            row.Add(factor);
            var holder = new VisualElement();
            holder.style.marginLeft = 8;
            row.Add(holder);
            Guid id = site.Id;
            // Red going out for what it sells, green coming in for what it buys, both for everything
            Timberborn.SliderToggleSystem.SliderToggleItem Item(int scope, string key, string icon) =>
                Timberborn.SliderToggleSystem.SliderToggleItem.Create(() => TradeText.T(key), _assets.Load<Sprite>("Sprites/Trade/" + icon),
                    () => _scopes[id] = scope, () => Scope(id) == scope);
            toggle = _sliderToggles.Create(holder,
                Item(0, "BeaverBuddies.Trade.Multiply.All", "TradeAll"),
                Item(1, "BeaverBuddies.Trade.Multiply.Sell", "TradeSell"),
                Item(2, "BeaverBuddies.Trade.Multiply.Buy", "TradeBuy"));
            // In their own colors, untinted by the toggle
            toggle.Root.Query<Image>("Icon").ForEach(i => i.tintColor = Color.white);
            return row;
        }

        private int Scope(Guid id) => _scopes.TryGetValue(id, out int scope) ? scope : 0;

        private void Multiply(ITradeSite site, float factor)
        {
            if (!site.Editable || !site.Entity) return;
            int scope = Scope(site.Id);
            foreach (string good in _goodService.Goods)
            {
                TradeRule rule = site.Rule(good);
                if (rule.Mode == TradeMode.Off || scope == 1 && rule.Mode != TradeMode.Sell || scope == 2 && rule.Mode != TradeMode.Buy) continue;
                // Never below 1: a value can't halve away to nothing
                int value = Mathf.Clamp(Mathf.RoundToInt(rule.Value * factor), 1, TradeRule.MostValue);
                if (value != rule.Value) site.Send(good, new TradeRule(rule.Mode, value, rule.Limit));
            }
        }

        private class Updater : IBatchControlRowItem, IUpdatableBatchControlRowItem
        {
            private readonly Action _update;

            public Updater(Action update)
            {
                _update = update;
                Root = new VisualElement();
                Root.style.display = DisplayStyle.None;
            }

            public VisualElement Root { get; }

            public void UpdateRowItem() => _update();
        }

        /**
         * One good at one post: its icon and name, how much of it there is, three buttons for what the post does with it,
         * and the two numbers, its value and how far it goes.
         */
        private class Card
        {
            private readonly ITradeSite _site;
            private readonly BaseComponent _entity;
            private readonly string _good;
            private readonly bool _canSet;
            private readonly Label _count;
            private readonly Label _limitLabel;
            private readonly IntegerField _value;
            private readonly IntegerField _limit;
            private readonly Dictionary<TradeMode, NineSliceButton> _modes = new();
            private TradeRule _shown;
            private bool _drawn;

            public VisualElement Root { get; }

            public Card(TradeGroups groups, ITradeSite site, string good)
            {
                _site = site;
                _entity = site.Entity;
                _good = good;
                _canSet = site.Editable;
                var described = groups._goodDescriber.GetDescribedGood(good);
                var root = new NineSliceVisualElement();
                root.AddToClassList("bg-sub-box--green");
                root.style.width = CardWidth;
                root.style.marginRight = root.style.marginBottom = 4;
                root.style.paddingLeft = root.style.paddingRight = 6;
                root.style.paddingTop = root.style.paddingBottom = 4;
                root.style.flexDirection = FlexDirection.Column;
                // The row item class gives a fixed height made for one line: a card has three
                root.style.height = StyleKeyword.Auto;
                root.style.minHeight = 80;
                root.style.flexShrink = 0;
                root.style.justifyContent = Justify.FlexStart;
                Root = root;

                var top = Pieces.Row();
                top.Add(Pieces.Icon(described.Icon, 24));
                var name = Pieces.Label(described.DisplayName, 13);
                name.style.flexGrow = 1;
                name.style.flexShrink = 1;
                name.style.marginLeft = 4;
                name.style.overflow = Overflow.Hidden;
                name.style.whiteSpace = WhiteSpace.NoWrap;
                top.Add(name);
                _count = Pieces.Label("", 12);
                _count.style.flexShrink = 0;
                top.Add(_count);
                groups._tooltips.Register(_count, () => _site.ShownTooltip(_good));
                root.Add(top);

                var modes = Pieces.Row();
                modes.style.marginTop = 3;
                foreach (TradeMode mode in new[] { TradeMode.Off, TradeMode.Buy, TradeMode.Sell })
                {
                    // The settlement panel's own buttons, like the Power Exchange's in the same tab
                    var button = new NineSliceButton();
                    button.AddToClassList("menu-button");
                    button.AddToClassList("menu-button--small");
                    button.text = TradeText.T("BeaverBuddies.Trade.Mode." + mode);
                    button.RegisterCallback<ClickEvent>(_ => SetMode(mode));
                    // A third of the card each, whatever width the class asks for
                    button.style.flexGrow = 0;
                    button.style.flexShrink = 0;
                    button.style.width = button.style.maxWidth = (CardWidth - 18) / 3;
                    button.style.minWidth = 0;
                    button.style.paddingLeft = button.style.paddingRight = 0;
                    button.style.height = 22;
                    button.style.marginLeft = button.style.marginRight = 1;
                    button.style.fontSize = 12;
                    groups._tooltips.Register(button, () => TradeText.T("BeaverBuddies.Trade.Mode." + mode + ".Tooltip"));
                    modes.Add(button);
                    _modes[mode] = button;
                }
                root.Add(modes);

                var numbers = Pieces.Row();
                numbers.style.marginTop = 3;
                var valueLabel = Pieces.Label(TradeText.T("BeaverBuddies.Trade.Value"), 12);
                numbers.Add(valueLabel);
                groups._tooltips.Register(valueLabel, () => TradeText.T("BeaverBuddies.Trade.Value.Tooltip"));
                _value = Pieces.Number(groups._loader, 44);
                _value.RegisterValueChangedCallback(evt =>
                {
                    int value = Mathf.Clamp(evt.newValue, 1, TradeRule.MostValue);
                    _value.SetValueWithoutNotify(value);
                    TradeRule rule = _site.Rule(_good);
                    Set(new TradeRule(rule.Mode, value, rule.Limit));
                });
                numbers.Add(_value);
                var gap = new VisualElement();
                gap.style.flexGrow = 1;
                numbers.Add(gap);
                _limitLabel = Pieces.Label("", 12);
                numbers.Add(_limitLabel);
                groups._tooltips.Register(_limitLabel, () => _site.LimitTooltip(_good));
                _limit = Pieces.Number(groups._loader, 52);
                _limit.RegisterValueChangedCallback(evt =>
                {
                    int limit = Mathf.Clamp(evt.newValue, 0, TradeRule.MostLimit);
                    _limit.SetValueWithoutNotify(limit);
                    TradeRule rule = _site.Rule(_good);
                    Set(new TradeRule(rule.Mode, rule.Value, limit));
                });
                numbers.Add(_limit);
                root.Add(numbers);
                Update();
            }

            private void SetMode(TradeMode mode)
            {
                TradeRule rule = _site.Rule(_good);
                if (rule.Mode == mode) return;
                // Turned on, it starts with what suits the way it goes; the value stays as it was set
                int limit = mode == TradeMode.Sell ? TradeRule.DefaultSellLimit : TradeRule.DefaultBuyLimit;
                Set(new TradeRule(mode, rule.Value, limit));
            }

            private void Set(TradeRule rule)
            {
                if (!_canSet || !_entity) return;
                _site.Send(_good, rule);
                Update();
            }

            public void Update()
            {
                if (!_entity) return;
                TradeRule rule = _site.Rule(_good);
                _count.text = rule.Mode == TradeMode.Off ? "" : _site.Shown(_good).ToString();
                if (_drawn && rule.Equals(_shown)) return;
                _drawn = true;
                _shown = rule;
                foreach (var mode in _modes)
                {
                    bool on = mode.Key == rule.Mode;
                    mode.Value.style.opacity = on ? 1f : 0.4f;
                    mode.Value.SetEnabled(_canSet);
                }
                bool active = rule.Mode != TradeMode.Off;
                _limitLabel.text = TradeText.T(rule.Mode == TradeMode.Buy ? "BeaverBuddies.Trade.Until" : "BeaverBuddies.Trade.Keep");
                Root.style.opacity = active ? 1f : 0.6f;
                Field(_value, rule.Value, active);
                Field(_limit, rule.Limit, active);
            }

            private void Field(IntegerField field, int value, bool active)
            {
                field.SetEnabled(active && _canSet);
                if (field.focusController?.focusedElement != field) field.SetValueWithoutNotify(value);
            }
        }
    }
}

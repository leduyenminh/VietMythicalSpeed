using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VMS.Backend;
using VMS.Core;
using VMS.Race;

namespace VMS.UI
{
    /// <summary>
    /// Tiệm Nén Vàng – the shop. Featured offers, gold packs (real money, through the platform store) and the
    /// eight cosmetic categories. Owned items can be equipped from here too, so it doubles as the wardrobe.
    /// Arg: a tab id, or "item:&lt;id&gt;" to open an item's details.
    /// </summary>
    public sealed class ShopScreen : UiScreen
    {
        public const string FeaturedTab = "featured";
        public const string GoldTab = "gold";

        static readonly string[] TabIds =
        {
            FeaturedTab, GoldTab, ShopCategories.AvatarFrame, ShopCategories.ProfileFrame, ShopCategories.BubbleFrame,
            ShopCategories.Exhaust, ShopCategories.NavigatorSkin, ShopCategories.Emotion, ShopCategories.Garage, ShopCategories.VesselKit,
        };

        static string _lastTab = FeaturedTab;

        readonly Dictionary<string, Button> _tabButtons = new Dictionary<string, Button>();
        RectTransform _panel, _content;
        ScrollRect _scroll;
        string _tab;

        public static string ItemArg(string itemId) => "item:" + itemId;

        protected override void Build(object arg)
        {
            Backdrop.Sky(Root);
            Ui.Header(Root, Loc.T("shop.title"), () => App.Ui.Back());
            CurrencyBar.Create(Root);

            string open = arg as string;
            ShopItem openItem = null;
            if (open != null && open.StartsWith("item:", StringComparison.Ordinal))
            {
                openItem = App.Shop.Item(open.Substring(5));
                open = openItem != null ? openItem.category : null;
            }
            if (open == ShopCategories.Special) open = FeaturedTab;

            var tabs = Ui.Node("Tabs", Root).At(0f, 1f, 40f, -150f, 330f, 880f, 0f, 1f);
            Ui.VStack(tabs, 10f);
            foreach (var id in TabIds)
            {
                string tabId = id;
                var b = Ui.Button(tabs, Loc.T("shop.tab." + id), () => SelectTab(tabId), ButtonStyle.Secondary, 26);
                b.Layout(-1f, 76f);
                _tabButtons[id] = b;
            }
            _panel = Ui.Card(Root, UiTheme.Panel.WithAlpha(0.95f), "Panel").rectTransform;
            _panel.Fill(400f, 150f, 40f, 40f);
            SelectTab(Array.IndexOf(TabIds, open) >= 0 ? open : _lastTab);
            if (openItem != null) ShowDetail(openItem);
        }

        public override void OnProfileChanged()
        {
            // Balances changed elsewhere (e.g. a purchase delivered late): refresh prices/ownership.
            if (_content != null) SelectTab(_tab);
        }

        void SelectTab(string id)
        {
            _tab = _lastTab = id;
            foreach (var kv in _tabButtons) StyleTab(kv.Value, kv.Key == id);
            float scrollPos = 1f;
            if (_scroll != null)
            {
                scrollPos = _scroll.verticalNormalizedPosition;
                Destroy(_scroll.gameObject);
            }
            _scroll = Ui.ScrollGrid(_panel, out _content, new Vector2(300f, 400f), new Vector2(22f, 22f));
            _scroll.GetComponent<RectTransform>().Fill(10f, 10f, 10f, 10f);
            if (id == GoldTab) BuildGold();
            else if (id == FeaturedTab) BuildFeatured();
            else
                foreach (var item in App.Shop.InCategory(id))
                    if (Listed(item)) ItemCard(item);
            Canvas.ForceUpdateCanvases();
            _scroll.verticalNormalizedPosition = scrollPos;
        }

        static void StyleTab(Button b, bool on)
        {
            b.transform.Find("Body").GetComponent<SkewQuad>().SetColors(on ? UiTheme.Hex("#4FB0FF") : Color.white, on ? UiTheme.Primary : UiTheme.PanelAlt);
            b.GetComponentInChildren<Text>().color = on ? Color.white : UiTheme.InkSoft;
        }

        bool Listed(ShopItem item) =>
            ShopRules.Owns(App.Profile, item) || (!item.hidden && (item.ForSale ? ShopRules.IsOnSale(item, DateTime.UtcNow) : item.basic));

        // ------------------------------------------------------------------ cosmetic items

        public static string PriceText(Price price) =>
            price == null ? string.Empty : RewardView.Glyph(price.currency) + " " + price.amount.ToString("N0");

        void ItemCard(ShopItem item)
        {
            var card = Ui.Card(_content, Color.white, "Item " + item.id);
            var rt = card.rectTransform;
            var stripe = Ui.Image(rt, UiSprites.RoundedSmall, UiTheme.RarityColor(Mathf.Max(1, item.rarity)), "Rarity");
            stripe.rectTransform.At(0.5f, 1f, 0f, -12f, 240f, 8f, 0.5f, 1f);
            var preview = Ui.Node("Preview", rt).At(0.5f, 1f, 0f, -28f, 270f, 210f, 0.5f, 1f);
            ShopPreviews.Small(preview, item);
            var name = Ui.FitLabel(rt, Loc.Pick(item.name), 28, UiTheme.Ink, TextAnchor.MiddleCenter);
            name.rectTransform.At(0.5f, 0f, 0f, 118f, 280f, 44f, 0.5f, 0f);
            string sub = ShopPreviews.TargetName(item);
            if (!string.IsNullOrEmpty(sub))
            {
                var t = Ui.FitLabel(rt, sub, 20, UiTheme.InkSoft, TextAnchor.MiddleCenter, FontStyle.Normal);
                t.rectTransform.At(0.5f, 0f, 0f, 90f, 280f, 30f, 0.5f, 0f);
            }
            var action = StateButton(rt, item, null);
            action.GetComponent<RectTransform>().At(0.5f, 0f, 0f, 14f, 250f, 68f, 0.5f, 0f);
            Ui.AddClick(card.gameObject, () => ShowDetail(item));
        }

        /// <summary>Buy / Equip / Equipped / Owned button for an item.</summary>
        Button StateButton(RectTransform parent, ShopItem item, Popup detail)
        {
            bool owned = ShopRules.Owns(App.Profile, item) && !item.consumable;
            if (owned && ShopCategories.IsEquippable(item.category))
            {
                var equipped = ShopRules.Equipped(App.Profile, App.Shop, item.category, item.target);
                bool isOn = equipped != null ? equipped.id == item.id : item.basic;
                var b = Ui.Button(parent, Loc.T(isOn ? "shop.equipped" : "shop.equip"), () => Equip(item, detail), isOn ? ButtonStyle.Secondary : ButtonStyle.Primary, 26);
                b.interactable = !isOn;
                return b;
            }
            if (owned)
            {
                var b = Ui.Button(parent, Loc.T("shop.owned"), null, ButtonStyle.Secondary, 26);
                b.interactable = false;
                return b;
            }
            if (ShopRules.IsOnSale(item, DateTime.UtcNow))
                return Ui.Button(parent, PriceText(item.price), () => Buy(item, detail), ButtonStyle.Accent, 28);
            var none = Ui.Button(parent, Loc.T("shop.fromEvents"), null, ButtonStyle.Secondary, 22);
            none.interactable = false;
            return none;
        }

        void Buy(ShopItem item, Popup detail)
        {
            var price = item.price;
            if (Wallet.Balance(App.Profile, price.currency) < price.amount)
            {
                if (price.currency == Currencies.Gold)
                    Popup.Confirm(Loc.T("shop.noGoldTitle"), Loc.T("shop.noGoldBody"), Loc.T("shop.getGold"), () =>
                    {
                        if (detail != null) detail.Close();
                        SelectTab(GoldTab);
                    });
                else
                    Popup.Message(Loc.T("common.error"), Loc.T("err.insufficient_" + price.currency));
                return;
            }
            Popup.Confirm(Loc.T("shop.confirmTitle"), Loc.F("shop.confirmBody", Loc.Pick(item.name), PriceText(price)), Loc.T("shop.buy"), async () =>
            {
                ShopBuyResponse r = null;
                if (!await App.Ui.Busy(Loc.T("common.working"), async () => r = await App.Backend.BuyItemAsync(item.id))) return;
                if (detail != null) detail.Close();
                App.SetProfile(r.profile);
                App.Ui.Toast(item.consumable ? Loc.F("shop.gotRewards", RewardView.Summary(r.rewards)) : Loc.F("shop.bought", Loc.Pick(item.name)));
                if (!item.consumable && ShopCategories.IsEquippable(item.category))
                    Popup.Confirm(Loc.Pick(item.name), Loc.T("shop.equipNow"), Loc.T("shop.equip"), () => Equip(item, null));
            });
        }

        async void Equip(ShopItem item, Popup detail)
        {
            PlayerProfile profile = null;
            if (!await App.Ui.Busy(Loc.T("common.working"), async () => profile = await App.Backend.EquipAsync(item.category, item.target, item.basic ? string.Empty : item.id))) return;
            if (detail != null) detail.Close();
            App.SetProfile(profile);
            App.Ui.Toast(Loc.F("shop.equippedToast", Loc.Pick(item.name)));
        }

        void ShowDetail(ShopItem item)
        {
            var p = Popup.Open(Loc.Pick(item.name), 1420f, 880f);
            var preview = Ui.Node("Preview", p.Body).At(0f, 0.5f, 0f, -10f, 660f, 660f, 0f, 0.5f);
            ShopPreviews.Large(preview, item);
            var info = Ui.Node("Info", p.Body).Fill(700f, 10f, 0f, 120f);
            Ui.VStack(info, 12f);
            var kind = Ui.Label(info, Loc.T("shop.tab." + item.category) + "  " + UiTheme.RarityStars(Mathf.Max(1, item.rarity)), 26, UiTheme.RarityColor(Mathf.Max(1, item.rarity)), TextAnchor.MiddleLeft, FontStyle.Bold);
            kind.Layout(-1f, 44f);
            string target = ShopPreviews.TargetName(item);
            if (!string.IsNullOrEmpty(target)) Ui.Label(info, Loc.F("shop.for", target), 26, UiTheme.InkSoft).Layout(-1f, 40f);
            Ui.Label(info, Loc.Pick(item.desc), 28, UiTheme.Ink, TextAnchor.UpperLeft).Layout(-1f, 180f);
            if (item.category == ShopCategories.NavigatorSkin && App.Profile != null && !App.Profile.Owns(item.target))
                Ui.Label(info, Loc.T("shop.skinNotOwned"), 24, UiTheme.Bad).Layout(-1f, 60f);
            if (item.ForSale && !ShopRules.Owns(App.Profile, item))
                Ui.Label(info, Loc.F("shop.balance", PriceText(new Price { currency = item.price.currency, amount = Wallet.Balance(App.Profile, item.price.currency) })), 24, UiTheme.InkSoft).Layout(-1f, 40f);
            var action = StateButton(p.Body, item, p);
            action.GetComponent<RectTransform>().At(1f, 0f, -20f, 10f, 420f, 96f, 1f, 0f);
        }

        // ------------------------------------------------------------------ gold, passes, bundles

        void BuildFeatured()
        {
            foreach (var product in SortedProducts())
                if (product.kind != "gold" && (product.limit <= 0 || ShopRules.CanBuyProduct(App.Profile, product))) ProductCard(product);
            ExchangeCard();
            foreach (var item in App.Shop.InCategory(ShopCategories.Special))
                if (Listed(item)) ItemCard(item);
        }

        void BuildGold()
        {
            if (!App.Purchases.Available) Notice(Loc.T(App.Backend.IsOnline ? "shop.storeUnavailable" : "shop.storeOffline"));
            foreach (var product in SortedProducts())
                if (product.kind == "gold") ProductCard(product);
            ExchangeCard();
        }

        List<ProductDef> SortedProducts()
        {
            var list = new List<ProductDef>(App.Shop.products);
            list.Sort((a, b) => a.sortOrder.CompareTo(b.sortOrder));
            return list;
        }

        void Notice(string text)
        {
            var card = Ui.Card(_content, UiTheme.PanelAlt, "Notice");
            Ui.Label(card.rectTransform, text, 24, UiTheme.InkSoft, TextAnchor.MiddleCenter).rectTransform.Fill(20f, 20f, 20f, 20f);
        }

        void ProductCard(ProductDef product)
        {
            var card = Ui.Card(_content, Color.white, "Product " + product.id);
            var rt = card.rectTransform;
            var glow = Ui.Image(rt, UiSprites.Glow, CurrencyBar.GoldColor.WithAlpha(0.35f), "Glow");
            glow.rectTransform.At(0.5f, 1f, 0f, -20f, 260f, 200f, 0.5f, 1f);
            glow.enabled = !UiTheme.Flat;
            bool first = ShopRules.PurchaseCount(App.Profile, product.id) == 0;
            if (product.kind == "gold")
            {
                var ingots = Ui.Label(rt, Ingots(product.gold), 64, CurrencyBar.GoldColor, TextAnchor.MiddleCenter, FontStyle.Bold);
                ingots.rectTransform.At(0.5f, 1f, 0f, -40f, 280f, 110f, 0.5f, 1f);
                var amount = Ui.Label(rt, product.gold.ToString("N0"), 52, UiTheme.Ink, TextAnchor.MiddleCenter, FontStyle.BoldAndItalic);
                amount.rectTransform.At(0.5f, 1f, 0f, -150f, 280f, 70f, 0.5f, 1f);
                int bonus = first && product.firstBonus > 0 ? product.firstBonus : product.bonus;
                if (bonus > 0)
                {
                    var badge = Ui.Skew(rt, first ? UiTheme.Lacquer : UiTheme.Primary, first ? UiTheme.Lacquer : UiTheme.PrimaryDark, 14f, "Bonus");
                    badge.rectTransform.At(0.5f, 1f, 0f, -222f, 260f, 48f, 0.5f, 1f);
                    var b = Ui.FitLabel(badge.rectTransform, Loc.F(first && product.firstBonus > 0 ? "shop.firstBonus" : "shop.bonus", bonus), 24, Color.white, TextAnchor.MiddleCenter);
                    b.rectTransform.Fill(10f, 2f, 10f, 2f);
                }
            }
            else
            {
                var icon = Ui.Image(rt, CosmeticArt.CellSprite(product.kind == "pass" ? VMS.Rendering.ProceduralTextures.FxCell.Lantern : VMS.Rendering.ProceduralTextures.FxCell.Sparkle), UiTheme.Hex("#FFB347"), "Icon");
                icon.rectTransform.At(0.5f, 1f, 0f, -30f, 140f, 140f, 0.5f, 1f);
                var title = Ui.FitLabel(rt, Loc.Pick(product.name), 28, UiTheme.Ink, TextAnchor.MiddleCenter);
                title.rectTransform.At(0.5f, 1f, 0f, -176f, 280f, 44f, 0.5f, 1f);
                string detail = Loc.Pick(product.desc);
                if (product.kind == "pass")
                {
                    int left = ShopRules.PassDaysLeft(App.Profile, product.id, DateTime.UtcNow);
                    if (left > 0) detail = Loc.F("shop.passLeft", left) + "\n" + detail;
                }
                var d = Ui.Label(rt, detail, 19, UiTheme.InkSoft, TextAnchor.UpperCenter);
                d.rectTransform.At(0.5f, 1f, 0f, -222f, 280f, 90f, 0.5f, 1f);
            }
            bool allowed = ShopRules.CanBuyProduct(App.Profile, product);
            var buy = Ui.Button(rt, allowed ? App.Purchases.PriceLabel(product) : Loc.T("shop.bought1"), () => BuyProduct(product), ButtonStyle.Accent, 30);
            buy.GetComponent<RectTransform>().At(0.5f, 0f, 0f, 14f, 250f, 72f, 0.5f, 0f);
            buy.interactable = allowed && App.Purchases.Available;
        }

        static string Ingots(int gold) => gold >= 3000 ? "▰▰▰\n▰▰▰" : gold >= 900 ? "▰▰\n▰▰▰" : gold >= 250 ? "▰▰▰" : "▰";

        async void BuyProduct(ProductDef product)
        {
            if (!App.Purchases.Available)
            {
                Popup.Message(Loc.T("shop.tab.gold"), Loc.T(App.Backend.IsOnline ? "shop.storeUnavailable" : "shop.storeOffline"));
                return;
            }
            PurchaseResponse r = null;
            if (!await App.Ui.Busy(Loc.T("shop.paying"), async () => r = await App.Purchases.BuyAsync(product))) return;
            App.SetProfile(r.profile);
            ShowReceipt(product, r);
        }

        static void ShowReceipt(ProductDef product, PurchaseResponse r)
        {
            var p = Popup.Open(Loc.T("shop.thanks"), 960f, 600f);
            var stack = Ui.Node("Receipt", p.Body).Fill(0f, 0f, 0f, 110f);
            Ui.VStack(stack, 14f);
            Ui.Label(stack, Loc.Pick(product.name), 32, UiTheme.Ink, TextAnchor.MiddleCenter, FontStyle.Bold).Layout(-1f, 50f);
            if (r.alreadyGranted) Ui.Label(stack, Loc.T("shop.alreadyDelivered"), 24, UiTheme.InkSoft, TextAnchor.MiddleCenter).Layout(-1f, 40f);
            var row = Ui.Node("Rewards", stack);
            row.Layout(-1f, 70f);
            Ui.HStack(row, 14f, 0, TextAnchor.MiddleCenter);
            var rewards = new List<Reward>();
            if (r.grant.gold > 0) rewards.Add(new Reward { type = Currencies.Gold, amount = r.grant.gold });
            if (r.grant.bonus > 0) rewards.Add(new Reward { type = Currencies.Gold, amount = r.grant.bonus });
            if (r.grant.passJade > 0) rewards.Add(new Reward { type = Currencies.Jade, amount = r.grant.passJade });
            if (r.grant.rewards != null) rewards.AddRange(r.grant.rewards);
            RewardView.Add(row, rewards, 64f);
            if (r.grant.bonus > 0) Ui.Label(stack, Loc.F(r.grant.firstPurchase ? "shop.receiptFirst" : "shop.receiptBonus", r.grant.bonus), 24, UiTheme.InkSoft, TextAnchor.MiddleCenter).Layout(-1f, 40f);
            var ok = Ui.Button(p.Body, Loc.T("common.ok"), p.Close, ButtonStyle.Primary);
            ok.GetComponent<RectTransform>().At(0.5f, 0f, 0f, 0f, 320f, 90f, 0.5f, 0f);
        }

        void ExchangeCard()
        {
            var card = Ui.Card(_content, Color.white, "Exchange");
            var rt = card.rectTransform;
            var from = Ui.Label(rt, CurrencyBar.GoldGlyph + "  →  ◆", 64, CurrencyBar.GoldColor, TextAnchor.MiddleCenter, FontStyle.Bold);
            from.rectTransform.At(0.5f, 1f, 0f, -50f, 280f, 110f, 0.5f, 1f);
            var title = Ui.FitLabel(rt, Loc.T("shop.exchange"), 28, UiTheme.Ink, TextAnchor.MiddleCenter);
            title.rectTransform.At(0.5f, 1f, 0f, -176f, 280f, 44f, 0.5f, 1f);
            var d = Ui.Label(rt, Loc.F("shop.exchangeHint", App.Shop.goldToJadeRate), 19, UiTheme.InkSoft, TextAnchor.UpperCenter);
            d.rectTransform.At(0.5f, 1f, 0f, -222f, 280f, 90f, 0.5f, 1f);
            var go = Ui.Button(rt, Loc.T("shop.exchangeGo"), ShowExchange, ButtonStyle.Primary, 28);
            go.GetComponent<RectTransform>().At(0.5f, 0f, 0f, 14f, 250f, 72f, 0.5f, 0f);
        }

        void ShowExchange()
        {
            int balance = App.Profile != null ? App.Profile.TotalGold : 0;
            int pull = App.Data.Gacha != null ? App.Data.Gacha.costPerPull : 150;
            var p = Popup.Open(Loc.T("shop.exchange"), 1000f, 620f);
            var stack = Ui.Node("Form", p.Body).Fill();
            Ui.VStack(stack, 16f);
            Ui.Label(stack, Loc.F("shop.exchangeBody", balance.ToString("N0"), App.Shop.goldToJadeRate), 26, UiTheme.Ink, TextAnchor.UpperLeft).Layout(-1f, 110f);
            var options = new[] { pull, pull * 10, balance };
            var labels = new[] { Loc.F("shop.exchangePulls", 1, pull), Loc.F("shop.exchangePulls", 10, pull * 10), Loc.F("shop.exchangeAll", balance.ToString("N0")) };
            for (int i = 0; i < options.Length; i++)
            {
                int amount = options[i];
                var b = Ui.Button(stack, labels[i], async () =>
                {
                    ExchangeResponse r = null;
                    if (!await App.Ui.Busy(Loc.T("common.working"), async () => r = await App.Backend.ExchangeGoldAsync(amount))) return;
                    p.Close();
                    App.SetProfile(r.profile);
                    App.Ui.Toast(Loc.F("shop.exchanged", r.jade.ToString("N0")));
                }, i == 2 ? ButtonStyle.Secondary : ButtonStyle.Primary, 28);
                b.Layout(-1f, 84f);
                b.interactable = amount > 0 && amount <= balance;
            }
        }
    }

    /// <summary>Small (card) and large (detail) previews of shop items, shared by the shop, garage and profile.</summary>
    public static class ShopPreviews
    {
        static GameApp App => GameApp.Instance;

        public static string TargetName(ShopItem item)
        {
            if (item.category == ShopCategories.NavigatorSkin)
            {
                var nav = App.Data.Navigator(item.target);
                return nav != null ? Loc.Pick(nav.name) : item.target;
            }
            if (item.category == ShopCategories.VesselKit) return Loc.T("vessel." + item.target);
            return null;
        }

        static RectTransform Centered(RectTransform area, RectTransform child)
        {
            child.anchorMin = child.anchorMax = child.pivot = new Vector2(0.5f, 0.5f);
            child.anchoredPosition = Vector2.zero;
            return child;
        }

        /// <summary>A 1920×1080 garage backdrop scaled into <paramref name="area"/> (clipped).</summary>
        static void MiniGarage(RectTransform area, ShopItem garage)
        {
            area.gameObject.AddComponent<RectMask2D>();
            var holder = Ui.Node("GarageView", area);
            Centered(area, holder);
            holder.sizeDelta = new Vector2(1920f, 1080f);
            float scale = Mathf.Max(area.rect.width / 1920f, area.rect.height / 1080f);
            if (scale <= 0f) scale = Mathf.Max(area.sizeDelta.x / 1920f, area.sizeDelta.y / 1080f);
            holder.localScale = Vector3.one * scale;
            CosmeticArt.Garage(holder, garage);
        }

        static Text Bubble(RectTransform area, ShopItem frame, string text, Vector2 size, int fontSize)
        {
            var bubble = Ui.Node("Bubble", area);
            Centered(area, bubble).sizeDelta = size;
            var bg = Ui.Image(bubble, UiSprites.Rounded, Color.white, "Bg");
            bg.rectTransform.Fill();
            var tail = Ui.Skew(bubble, Color.white, Color.white, -14f, "Tail");
            tail.rectTransform.At(0f, 0.5f, 6f, -size.y * 0.15f, 30f, 26f, 1f, 0.5f);
            var line = Ui.Label(bubble, text, fontSize, UiTheme.Ink, TextAnchor.MiddleCenter);
            line.rectTransform.Fill(20f, 10f, 20f, 10f);
            CosmeticArt.ApplyBubble(bubble, bg, tail, line, frame);
            return line;
        }

        static void Plate(RectTransform area, ShopItem plate, float width, float height, int fontSize)
        {
            var holder = Ui.Node("PlateView", area);
            Centered(area, holder).sizeDelta = new Vector2(width, height);
            CosmeticArt.Plate(holder, plate, height * 0.3f).Fill();
            var account = App.Auth.Account;
            var name = Ui.FitLabel(holder, account != null ? account.displayName : "Envoy", fontSize, CosmeticArt.PlateText(plate), TextAnchor.MiddleCenter);
            name.rectTransform.Fill(height * 0.4f, 6f, height * 0.4f, 6f);
        }

        public static void Small(RectTransform area, ShopItem item)
        {
            var nav = App.SelectedNavigator;
            switch (item.category)
            {
                case ShopCategories.AvatarFrame:
                    Centered(area, CosmeticArt.Avatar(area, nav, item, 190f));
                    break;
                case ShopCategories.ProfileFrame:
                    Plate(area, item, 260f, 96f, 30);
                    break;
                case ShopCategories.BubbleFrame:
                    Bubble(area, item, Loc.T("shop.sampleShort"), new Vector2(250f, 110f), 24);
                    break;
                case ShopCategories.Exhaust:
                    Centered(area, CosmeticArt.ExhaustSwatch(area, item, 260f, 120f, UiTheme.Hex("#7CF3FF")));
                    break;
                case ShopCategories.NavigatorSkin:
                    Centered(area, NavigatorArt.Portrait(area, App.Data.Navigator(item.target), 200f, false, item));
                    break;
                case ShopCategories.Emotion:
                    Centered(area, CosmeticArt.FaceIcon(area, item.style != null ? item.style.face : null, 170f));
                    break;
                case ShopCategories.Garage:
                    area.sizeDelta = new Vector2(270f, 200f);
                    MiniGarage(area, item);
                    break;
                case ShopCategories.VesselKit:
                    KitSwatch(area, item, 70f);
                    break;
                default:
                    SpecialIcon(area, item, 80);
                    break;
            }
        }

        public static void Large(RectTransform area, ShopItem item)
        {
            var nav = App.SelectedNavigator;
            switch (item.category)
            {
                case ShopCategories.AvatarFrame:
                    Centered(area, CosmeticArt.Avatar(area, nav, item, 440f));
                    break;
                case ShopCategories.ProfileFrame:
                    Plate(area, item, 600f, 170f, 52);
                    break;
                case ShopCategories.BubbleFrame:
                    var figure = NavigatorArt.Standing(area, nav, 560f);
                    figure.At(0.3f, 0f, 0f, 0f, figure.sizeDelta.x, figure.sizeDelta.y, 0.5f, 0f);
                    var holder = Ui.Node("BubbleHolder", area).At(0.5f, 1f, 70f, -40f, 520f, 170f, 0.5f, 1f);
                    Bubble(holder, item, Loc.T("shop.sampleLine"), new Vector2(520f, 170f), 30);
                    break;
                case ShopCategories.NavigatorSkin:
                    var skinned = NavigatorArt.Standing(area, App.Data.Navigator(item.target), 640f, item);
                    Centered(area, skinned);
                    break;
                case ShopCategories.Emotion:
                    var fig = NavigatorArt.Standing(area, nav, 600f);
                    fig.At(0.4f, 0f, 0f, 0f, fig.sizeDelta.x, fig.sizeDelta.y, 0.5f, 0f);
                    NavigatorArt.SetEmotion(fig, item.style != null ? item.style.face : null);
                    var bubbleHolder = Ui.Node("BubbleHolder", area).At(0.5f, 1f, 120f, -20f, 420f, 130f, 0.5f, 1f);
                    Bubble(bubbleHolder, CosmeticArt.Equipped(ShopCategories.BubbleFrame), item.style != null ? Loc.Pick(item.style.text) : string.Empty, new Vector2(420f, 130f), 30);
                    break;
                case ShopCategories.Garage:
                case ShopCategories.Exhaust:
                case ShopCategories.VesselKit:
                    area.sizeDelta = new Vector2(660f, 660f);
                    var garage = item.category == ShopCategories.Garage ? item : CosmeticArt.Equipped(ShopCategories.Garage);
                    MiniGarage(area, garage);
                    string vesselId = item.category == ShopCategories.VesselKit ? item.target : App.Settings.Vessel;
                    var baseDef = VesselDef.Find(vesselId);
                    var kit = item.category == ShopCategories.VesselKit ? item : CosmeticArt.Equipped(ShopCategories.VesselKit, baseDef.id);
                    var def = VesselStyles.WithKit(baseDef, kit);
                    var exhaust = item.category == ShopCategories.Exhaust ? item : CosmeticArt.Equipped(ShopCategories.Exhaust);
                    var view = Ui.Node("Vessel", area).Fill();
                    VesselPreview.Create(view, 900, 900).Show(def, ExhaustStyle.From(exhaust, def));
                    break;
                default:
                    SpecialIcon(area, item, 200);
                    break;
            }
        }

        static void KitSwatch(RectTransform area, ShopItem kit, float dot)
        {
            var s = kit.style;
            var row = Ui.Node("Swatches", area).At(0.5f, 0.5f, 0f, 16f, dot * 3.6f, dot);
            Ui.HStack(row, dot * 0.3f, 0, TextAnchor.MiddleCenter);
            foreach (var hex in new[] { s != null ? s.colorA : null, s != null ? s.colorB : null, s != null ? s.colorC : null })
            {
                var c = Ui.Image(row, UiSprites.Circle, CosmeticArt.Hex(hex, Color.gray), "Swatch");
                c.Layout(dot, dot);
            }
            if (s != null && !string.IsNullOrEmpty(s.ornament) && s.ornament != "none")
            {
                var o = Ui.Label(area, Loc.T("shop.ornament." + s.ornament), 22, UiTheme.InkSoft, TextAnchor.MiddleCenter, FontStyle.Bold);
                o.rectTransform.At(0.5f, 0.5f, 0f, -dot * 0.75f, 260f, 34f);
            }
        }

        static void SpecialIcon(RectTransform area, ShopItem item, int size)
        {
            var reward = item.rewards != null && item.rewards.Count > 0 ? item.rewards[0] : null;
            string type = reward != null ? reward.type : Currencies.Coins;
            var glow = Ui.Image(area, UiSprites.Glow, RewardView.ColorOf(type).WithAlpha(0.45f), "Glow");
            Centered(area, glow.rectTransform).sizeDelta = new Vector2(size * 2.2f, size * 2.2f);
            glow.enabled = !UiTheme.Flat;
            var icon = Ui.Image(area, UiSprites.Circle, RewardView.ColorOf(type), "Icon");
            Centered(area, icon.rectTransform).sizeDelta = new Vector2(size * 1.3f, size * 1.3f);
            var glyph = Ui.Label(icon.rectTransform, RewardView.Glyph(type), size, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            glyph.rectTransform.Fill();
            if (reward != null)
            {
                var amount = Ui.Label(area, reward.amount.ToString("N0"), Mathf.RoundToInt(size * 0.45f), UiTheme.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
                amount.rectTransform.At(0.5f, 0f, 0f, 0f, 260f, size * 0.6f, 0.5f, 0f);
            }
        }
    }
}

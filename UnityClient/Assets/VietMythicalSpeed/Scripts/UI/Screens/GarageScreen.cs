using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VMS.Core;
using VMS.Race;

namespace VMS.UI
{
    /// <summary>
    /// Nhà Xưởng – the garage: the chosen spirit vessel turning in the equipped interior, with tabs to switch
    /// vessel, kit, exhaust and interior. Items not owned yet open in the shop.
    /// </summary>
    public sealed class GarageScreen : UiScreen
    {
        static readonly string[] Tabs = { "vessel", ShopCategories.VesselKit, ShopCategories.Exhaust, ShopCategories.Garage };
        static int _lastTab;

        VesselPreview _preview;

        public VesselPreview Preview => _preview;
        RectTransform _backdrop, _list;
        ScrollRect _scroll;
        int _tab;

        protected override void Build(object arg)
        {
            _backdrop = Ui.Node("Backdrop", Root).Fill(-200f, -200f, -200f, -200f);
            _backdrop.SetAsFirstSibling();
            Ui.Header(Root, Loc.T("garage.title"), () => App.Ui.Back());
            CurrencyBar.Create(Root);

            var stage = Ui.Node("Stage", Root).Fill(0f, 130f, 760f, 0f);
            _preview = VesselPreview.Create(stage, 1280, 1000);
            var caption = Ui.Label(Root, string.Empty, 34, UiTheme.Ink, TextAnchor.LowerLeft, FontStyle.BoldAndItalic, "Caption");
            caption.rectTransform.At(0f, 0f, 60f, 40f, 1000f, 60f, 0f, 0f);
            caption.gameObject.AddComponent<Shadow>().effectColor = Color.white.WithAlpha(0.6f);
            _caption = caption;

            var panel = Ui.Card(Root, UiTheme.Panel.WithAlpha(0.95f), "Panel").rectTransform;
            panel.At(1f, 0.5f, -40f, -50f, 700f, 900f, 1f, 0.5f);
            var labels = new string[Tabs.Length];
            for (int i = 0; i < Tabs.Length; i++) labels[i] = Loc.T("garage.tab." + Tabs[i]);
            var seg = Ui.Node("Tabs", panel).At(0.5f, 1f, 0f, -24f, 640f, 64f, 0.5f, 1f);
            var segments = Ui.Segmented(seg, labels, _lastTab, SelectTab, 22);
            ((RectTransform)segments[0].transform.parent).Fill();
            _list = Ui.Node("List", panel).Fill(20f, 104f, 20f, 20f);
            Refresh();
            SelectTab(_lastTab);
        }

        Text _caption;

        string VesselId => App.Settings.Vessel;

        public override void OnProfileChanged()
        {
            Refresh();
            SelectTab(_tab);
        }

        /// <summary>Backdrop, preview and caption from what is equipped now.</summary>
        void Refresh()
        {
            for (int i = _backdrop.childCount - 1; i >= 0; i--) Destroy(_backdrop.GetChild(i).gameObject);
            CosmeticArt.Garage(_backdrop, CosmeticArt.Equipped(ShopCategories.Garage));
            var baseDef = VesselDef.Find(VesselId);
            var kit = CosmeticArt.Equipped(ShopCategories.VesselKit, baseDef.id);
            var def = VesselStyles.WithKit(baseDef, kit);
            var exhaust = CosmeticArt.Equipped(ShopCategories.Exhaust);
            _preview.Show(def, ExhaustStyle.From(exhaust, def));
            _caption.text = Loc.T(baseDef.nameKey) + (kit != null ? " · " + Loc.Pick(kit.name) : string.Empty);
        }

        void SelectTab(int index)
        {
            _tab = _lastTab = index;
            if (_scroll != null) Destroy(_scroll.gameObject);
            _scroll = Ui.ScrollList(_list, out var content, 12f, 6);
            _scroll.GetComponent<RectTransform>().Fill();
            switch (Tabs[index])
            {
                case "vessel":
                    foreach (var v in VesselDef.All) VesselRow(content, v);
                    break;
                case ShopCategories.VesselKit:
                    StockRow(content);
                    foreach (var item in App.Shop.InCategory(ShopCategories.VesselKit))
                        if (item.target == VesselId) ItemRow(content, item);
                    break;
                default:
                    foreach (var item in App.Shop.InCategory(Tabs[index])) ItemRow(content, item);
                    break;
            }
        }

        RectTransform Row(RectTransform content, string title, string subtitle, out RectTransform icon)
        {
            var row = Ui.Card(content, Color.white, "Row " + title).rectTransform;
            row.Layout(-1f, 120f);
            icon = Ui.Node("Icon", row).At(0f, 0.5f, 16f, 0f, 150f, 100f, 0f, 0.5f);
            var t = Ui.FitLabel(row, title, 30, UiTheme.Ink, TextAnchor.MiddleLeft);
            t.rectTransform.Fill(180f, 14f, 220f, 52f);
            if (!string.IsNullOrEmpty(subtitle))
            {
                var s = Ui.Label(row, subtitle, 22, UiTheme.InkSoft, TextAnchor.MiddleLeft);
                s.rectTransform.Fill(180f, 62f, 220f, 12f);
            }
            return row;
        }

        void VesselRow(RectTransform content, VesselDef v)
        {
            var kit = CosmeticArt.Equipped(ShopCategories.VesselKit, v.id);
            var row = Row(content, Loc.T(v.nameKey), kit != null ? Loc.Pick(kit.name) : Loc.T("garage.stock"), out var icon);
            var dots = Ui.Node("Colours", icon).Fill();
            Ui.HStack(dots, 6f, 0, TextAnchor.MiddleCenter);
            var styled = VesselStyles.WithKit(v, kit);
            foreach (var c in new[] { styled.body, styled.trim, styled.glow }) Ui.Image(dots, UiSprites.Circle, c, "Dot").Layout(40f, 40f);
            bool current = v.id == VesselId;
            var b = Ui.Button(row, Loc.T(current ? "shop.equipped" : "garage.ride"), () =>
            {
                App.Settings.Vessel = v.id;
                App.Settings.Save();
                Refresh();
                SelectTab(_tab);
            }, current ? ButtonStyle.Secondary : ButtonStyle.Primary, 24);
            b.GetComponent<RectTransform>().At(1f, 0.5f, -16f, 0f, 190f, 70f, 1f, 0.5f);
            b.interactable = !current;
        }

        void StockRow(RectTransform content)
        {
            var v = VesselDef.Find(VesselId);
            var row = Row(content, Loc.T("garage.stock"), Loc.T(v.nameKey), out var icon);
            var dots = Ui.Node("Colours", icon).Fill();
            Ui.HStack(dots, 6f, 0, TextAnchor.MiddleCenter);
            foreach (var c in new[] { v.body, v.trim, v.glow }) Ui.Image(dots, UiSprites.Circle, c, "Dot").Layout(40f, 40f);
            bool current = CosmeticArt.Equipped(ShopCategories.VesselKit, v.id) == null;
            var b = Ui.Button(row, Loc.T(current ? "shop.equipped" : "shop.equip"), () => Equip(ShopCategories.VesselKit, v.id, string.Empty), current ? ButtonStyle.Secondary : ButtonStyle.Primary, 24);
            b.GetComponent<RectTransform>().At(1f, 0.5f, -16f, 0f, 190f, 70f, 1f, 0.5f);
            b.interactable = !current;
        }

        void ItemRow(RectTransform content, ShopItem item)
        {
            bool owned = ShopRules.Owns(App.Profile, item);
            if (!owned && (item.hidden || !ShopRules.IsOnSale(item, DateTime.UtcNow))) return;
            var row = Row(content, Loc.Pick(item.name), owned ? null : ShopScreen.PriceText(item.price), out var icon);
            switch (item.category)
            {
                case ShopCategories.Exhaust:
                    var swatch = CosmeticArt.ExhaustSwatch(icon, item, 150f, 80f, VesselDef.Find(VesselId).glow);
                    swatch.anchorMin = swatch.anchorMax = new Vector2(0.5f, 0.5f);
                    swatch.anchoredPosition = Vector2.zero;
                    break;
                case ShopCategories.Garage:
                    var mini = Ui.Node("Mini", icon).Fill();
                    mini.gameObject.AddComponent<RectMask2D>();
                    var holder = Ui.Node("Holder", mini).At(0.5f, 0.5f, 0f, 0f, 1920f, 1080f);
                    holder.localScale = Vector3.one * (150f / 1920f) * 1.2f;
                    CosmeticArt.Garage(holder, item);
                    break;
                default:
                    var dots = Ui.Node("Colours", icon).Fill();
                    Ui.HStack(dots, 6f, 0, TextAnchor.MiddleCenter);
                    var s = item.style;
                    foreach (var hex in new[] { s != null ? s.colorA : null, s != null ? s.colorB : null, s != null ? s.colorC : null })
                        Ui.Image(dots, UiSprites.Circle, CosmeticArt.Hex(hex, Color.gray), "Dot").Layout(40f, 40f);
                    break;
            }
            Button b;
            if (owned)
            {
                var equipped = ShopRules.Equipped(App.Profile, App.Shop, item.category, item.target);
                bool current = equipped != null ? equipped.id == item.id : item.basic;
                b = Ui.Button(row, Loc.T(current ? "shop.equipped" : "shop.equip"), () => Equip(item.category, item.target, item.basic ? string.Empty : item.id), current ? ButtonStyle.Secondary : ButtonStyle.Primary, 24);
                b.interactable = !current;
            }
            else
            {
                b = Ui.Button(row, Loc.T("garage.view"), () => App.Ui.Show<ShopScreen>(ShopScreen.ItemArg(item.id)), ButtonStyle.Accent, 24);
            }
            b.GetComponent<RectTransform>().At(1f, 0.5f, -16f, 0f, 190f, 70f, 1f, 0.5f);
        }

        async void Equip(string category, string target, string itemId)
        {
            PlayerProfile profile = null;
            if (!await App.Ui.Busy(Loc.T("common.working"), async () => profile = await App.Backend.EquipAsync(category, target, itemId))) return;
            App.SetProfile(profile);
        }
    }
}

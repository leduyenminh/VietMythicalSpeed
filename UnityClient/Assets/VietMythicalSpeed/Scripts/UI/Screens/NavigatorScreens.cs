using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VMS.Core;

namespace VMS.UI
{
    public sealed class NavigatorsScreen : UiScreen
    {
        static int _filter;
        RectTransform _grid;

        protected override void Build(object arg)
        {
            Backdrop.Sky(Root);
            Ui.Header(Root, Loc.T("navigators.title"), () => App.Ui.Back());
            CurrencyBar.Create(Root);

            var filters = Ui.Node("Filters", Root).At(0f, 1f, 60f, -150f, 900f, 70f, 0f, 1f);
            var seg = Ui.Segmented(filters, new[] { Loc.T("navigators.all"), "★★★", "★★", "★" }, _filter, i =>
            {
                _filter = i;
                Populate();
            }, 26);
            seg[0].transform.parent.GetComponent<RectTransform>().Fill();

            int owned = 0;
            foreach (var n in App.Data.AllNavigators()) if (App.Profile != null && App.Profile.Owns(n.id)) owned++;
            var count = Ui.Label(Root, Loc.F("navigators.count", owned, App.Data.Navigators.navigators.Length), 26, UiTheme.InkSoft, TextAnchor.MiddleRight);
            count.rectTransform.At(1f, 1f, -60f, -150f, 600f, 70f, 1f, 1f);

            var area = Ui.Card(Root, UiTheme.Panel.WithAlpha(0.8f), "GridCard").rectTransform;
            area.Fill(40f, 240f, 40f, 40f);
            Ui.ScrollGrid(area, out _grid, new Vector2(250f, 340f), new Vector2(24f, 24f)).GetComponent<RectTransform>().Fill(6f, 6f, 6f, 6f);
            Populate();
        }

        void Populate()
        {
            for (int i = _grid.childCount - 1; i >= 0; i--) Destroy(_grid.GetChild(i).gameObject);
            var list = new List<NavigatorDef>(App.Data.AllNavigators());
            // Owned first, then rarity, then catalogue order.
            list.Sort((a, b) =>
            {
                bool oa = App.Profile.Owns(a.id), ob = App.Profile.Owns(b.id);
                if (oa != ob) return oa ? -1 : 1;
                return b.rarity.CompareTo(a.rarity);
            });
            foreach (var def in list)
            {
                if (_filter > 0 && def.rarity != 4 - _filter) continue;
                bool owned = App.Profile.Owns(def.id);
                var cell = Ui.Node("Cell " + def.id, _grid);
                var portrait = NavigatorArt.Portrait(cell, def, 230f, !owned);
                portrait.At(0.5f, 1f, 0f, 0f, 230f, 230f, 0.5f, 1f);
                NavigatorArt.NamePlate(cell, def, 240f, !owned);
                cell.GetChild(cell.childCount - 1).GetComponent<RectTransform>().At(0.5f, 0f, 0f, 10f, 240f, 90f, 0.5f, 0f);
                if (App.Profile.selectedNavigator == def.id)
                {
                    var badge = Ui.Skew(cell, UiTheme.Accent, UiTheme.AccentDark, 12f, "Selected");
                    badge.rectTransform.At(0f, 1f, 6f, -6f, 120f, 40f, 0f, 1f);
                    Ui.Label(badge.rectTransform, Loc.T("navigators.partner"), 20, UiTheme.Ink, TextAnchor.MiddleCenter, FontStyle.Bold).rectTransform.Fill();
                }
                var hit = Ui.Image(cell, UiSprites.White, new Color(0, 0, 0, 0), "Hit");
                hit.rectTransform.Fill();
                hit.raycastTarget = true;
                var captured = def;
                Ui.AddClick(hit.gameObject, () => App.Ui.Show<NavigatorDetailScreen>(captured));
            }
        }

        public override void OnProfileChanged() => Populate();
    }

    public sealed class NavigatorDetailScreen : UiScreen
    {
        NavigatorDef _def;
        RectTransform _info, _figure;
        Text _bubble;

        protected override void Build(object arg)
        {
            _def = (NavigatorDef)arg;
            Backdrop.Sky(Root);
            Ui.Header(Root, Loc.Pick(_def.name), () => App.Ui.Back());
            CurrencyBar.Create(Root);

            var bubbleRoot = Ui.Node("Bubble", Root).At(0.24f, 1f, 0f, -150f, 640f, 120f, 0.5f, 1f);
            Ui.Image(bubbleRoot, UiSprites.Rounded, Color.white.WithAlpha(0.95f), "Bg").rectTransform.Fill();
            _bubble = Ui.Label(bubbleRoot, string.Empty, 28, UiTheme.Ink, TextAnchor.MiddleCenter);
            _bubble.rectTransform.Fill(24f, 10f, 24f, 10f);
            bubbleRoot.gameObject.SetActive(false);

            _info = Ui.Card(Root, UiTheme.Panel.WithAlpha(0.97f), "Info").rectTransform;
            _info.Region(0.45f, 0f, 1f, 1f);
            _info.offsetMin = new Vector2(0f, 40f);
            _info.offsetMax = new Vector2(-40f, -140f);
            Populate();
        }

        public override void OnProfileChanged() => Populate();

        void Populate()
        {
            // The figure shows the equipped outfit, so it is rebuilt with the rest.
            if (_figure != null) Destroy(_figure.gameObject);
            _figure = NavigatorArt.Standing(Root, _def, 900f);
            _figure.At(0.24f, 0f, 0f, 0f, 560f, 900f, 0.5f, 0f);
            _figure.SetSiblingIndex(1);
            for (int i = _info.childCount - 1; i >= 0; i--)
                if (_info.GetChild(i).name != "Shadow") Destroy(_info.GetChild(i).gameObject);

            var owned = App.Profile.FindNavigator(_def.id);
            int awakening = owned != null ? owned.awakening : 0;
            Ui.ScrollList(_info, out var content, 14f, 30).GetComponent<RectTransform>().Fill(0f, 0f, 0f, 130f);

            var title = Ui.Label(content, Loc.Pick(_def.title), 30, UiTheme.NavigatorColor(_def), TextAnchor.MiddleLeft, FontStyle.Bold);
            title.Layout(-1f, 44f);
            var stars = Ui.Label(content, UiTheme.RarityStars(_def.rarity) + "  " + UiTheme.RarityName(_def.rarity) + (_def.limited ? "  ·  " + Loc.T("navigators.limited") : string.Empty),
                28, UiTheme.RarityColor(_def.rarity), TextAnchor.MiddleLeft, FontStyle.Bold);
            stars.Layout(-1f, 40f);
            var facts = Ui.Label(content, Loc.T("navigators.creature") + ": " + Loc.Pick(_def.creature) + "\n" + Loc.T("navigators.region") + ": " + Loc.Pick(_def.region), 24, UiTheme.InkSoft);
            facts.Layout(-1f, 70f);
            var bio = Ui.Label(content, Loc.Pick(_def.bio), 27, UiTheme.Ink);
            bio.Layout(-1f, 150f);

            if (_def.skill != null)
            {
                Section(content, Loc.T("navigators.skill") + " · " + Loc.Pick(_def.skill.name));
                var s = Ui.Label(content, Format.Skill(Loc.Pick(_def.skill.desc), _def.skill.ValueAt(awakening), _def.skill.DurationAt(awakening), _def.skill.type) +
                    "  (" + Loc.F("navigators.cost", Mathf.RoundToInt(_def.skill.cost)) + ")", 26, UiTheme.Ink);
                s.Layout(-1f, 76f);
            }
            if (_def.passive != null)
            {
                Section(content, Loc.T("navigators.passive"));
                var p = Ui.Label(content, Format.Skill(Loc.Pick(_def.passive.desc), _def.passive.ValueAt(awakening), 0f, _def.passive.type) + "  " + Loc.T("navigators.skillModeOnly"), 26, UiTheme.Ink);
                p.Layout(-1f, 70f);
            }

            if (owned != null)
            {
                var bondCfg = App.Data.Economy.bond;
                int level = EconomyRules.BondLevel(bondCfg, owned.bondExp);
                Section(content, Loc.F("navigators.bond", level, EconomyRules.MaxBondLevel(bondCfg)));
                var barRoot = Ui.Node("BondBar", content);
                barRoot.Layout(-1f, 26f);
                Ui.Image(barRoot, UiSprites.RoundedSmall, UiTheme.Line, "Track").rectTransform.Fill();
                var fill = Ui.Image(barRoot, UiSprites.RoundedSmall, UiTheme.Lacquer, "Fill");
                float lo = bondCfg.levelExp[level - 1];
                float hi = level < bondCfg.levelExp.Length ? bondCfg.levelExp[level] : lo + 1f;
                float frac = level >= bondCfg.levelExp.Length ? 1f : Mathf.Clamp01((owned.bondExp - lo) / Mathf.Max(1f, hi - lo));
                fill.rectTransform.Region(0f, 0f, Mathf.Max(0.02f, frac), 1f);

                Section(content, Loc.F("navigators.awakening", AwakeningStars(awakening, App.Data.Gacha.maxAwaken)));
                var cost = CollectionRules.CostToAwaken(App.Profile, _def.id, App.Data.Gacha);
                var awakenInfo = Ui.Label(content, cost.possible
                    ? Loc.F("navigators.awakenCost", owned.fragments, cost.fragments, cost.coins.ToString("N0"))
                    : Loc.T("navigators.awakenMax"), 24, UiTheme.InkSoft);
                awakenInfo.Layout(-1f, 40f);

                var outfits = App.Shop.InCategory(ShopCategories.NavigatorSkin).FindAll(x => x.target == _def.id);
                if (outfits.Count > 0)
                {
                    Section(content, Loc.T("navigators.outfits"));
                    OutfitRow(content, null);
                    foreach (var skin in outfits) OutfitRow(content, skin);
                }

                Section(content, Loc.T("navigators.voice"));
                foreach (var line in _def.voice)
                {
                    bool unlocked = level >= line.minBond;
                    var row = Ui.Button(content, unlocked ? Loc.T("voice." + line.trigger) : Loc.F("navigators.voiceLocked", line.minBond),
                        unlocked ? () => Say(line) : (System.Action)null, ButtonStyle.Secondary, 24);
                    row.Layout(-1f, 64f);
                    row.interactable = unlocked;
                }

                var buttons = Ui.Node("Actions", _info).At(0.5f, 0f, 0f, 30f, 900f, 96f, 0.5f, 0f);
                Ui.HStack(buttons, 24f, 0, TextAnchor.MiddleCenter);
                var awaken = Ui.Button(buttons, Loc.T("navigators.awaken"), Awaken, ButtonStyle.Primary, 30);
                awaken.Layout(360f, 96f);
                awaken.interactable = cost.possible && cost.affordable;
                bool isPartner = App.Profile.selectedNavigator == _def.id;
                var select = Ui.Button(buttons, isPartner ? Loc.T("navigators.isPartner") : Loc.T("navigators.setPartner"), SetPartner, ButtonStyle.Accent, 30);
                select.Layout(420f, 96f);
                select.interactable = !isPartner;
            }
            else
            {
                var locked = Ui.Label(_info, Loc.T("navigators.notOwned"), 28, UiTheme.InkSoft, TextAnchor.MiddleCenter, FontStyle.Italic);
                locked.rectTransform.At(0.5f, 0f, 0f, 50f, 900f, 70f, 0.5f, 0f);
            }
        }

        /// <summary>Base outfit (skin == null) or a shop skin: equip when owned, otherwise open it in the shop.</summary>
        void OutfitRow(RectTransform content, ShopItem skin)
        {
            var equipped = NavigatorArt.EquippedSkin(_def);
            bool isOn = skin == null ? equipped == null : equipped != null && equipped.id == skin.id;
            bool owned = skin == null || ShopRules.Owns(App.Profile, skin);
            if (!owned && (skin.hidden || !ShopRules.IsOnSale(skin, System.DateTime.UtcNow))) return;
            string state = isOn ? Loc.T("shop.equipped") : owned ? Loc.T("shop.equip") : ShopScreen.PriceText(skin.price);
            var row = Ui.Button(content, (skin == null ? Loc.T("navigators.baseOutfit") : Loc.Pick(skin.name)) + "   ·   " + state, () =>
            {
                if (!owned) App.Ui.Show<ShopScreen>(ShopScreen.ItemArg(skin.id));
                else EquipOutfit(skin);
            }, isOn ? ButtonStyle.Secondary : owned ? ButtonStyle.Primary : ButtonStyle.Accent, 24);
            row.Layout(-1f, 64f);
            row.interactable = !isOn;
        }

        async void EquipOutfit(ShopItem skin)
        {
            PlayerProfile profile = null;
            if (!await App.Ui.Busy(Loc.T("common.working"), async () => profile = await App.Backend.EquipAsync(ShopCategories.NavigatorSkin, _def.id, skin != null ? skin.id : string.Empty))) return;
            App.SetProfile(profile);
        }

        static string AwakeningStars(int level, int max) => new string('◆', level) + new string('◇', Mathf.Max(0, max - level));

        static void Section(RectTransform content, string title)
        {
            var t = Ui.Label(content, title, 28, UiTheme.Primary, TextAnchor.LowerLeft, FontStyle.Bold);
            t.Layout(-1f, 48f);
        }

        void Say(VoiceLine line)
        {
            _bubble.text = Loc.Pick(line.text);
            _bubble.transform.parent.gameObject.SetActive(true);
            var clip = NavigatorArt.Voice(_def.id, line.id);
            if (clip != null) App.Audio.Voice(clip);
        }

        async void Awaken()
        {
            await App.Ui.Busy(Loc.T("common.working"), async () => App.SetProfile(await App.Backend.AwakenAsync(_def.id)));
        }

        async void SetPartner()
        {
            bool ok = await App.Ui.Busy(Loc.T("common.working"), async () => App.SetProfile(await App.Backend.SelectNavigatorAsync(_def.id)));
            if (!ok) return;
            App.Ui.Toast(Loc.F("navigators.partnerSet", Loc.Pick(_def.name)));
            var greet = new List<VoiceLine>(_def.LinesFor(VoiceTriggers.Greet, 1));
            if (greet.Count > 0) Say(greet[0]);
        }
    }
}

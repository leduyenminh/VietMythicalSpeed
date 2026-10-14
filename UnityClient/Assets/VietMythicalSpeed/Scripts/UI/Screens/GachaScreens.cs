using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using VMS.Backend;
using VMS.Core;

namespace VMS.UI
{
    /// <summary>Đền Hoa Tiêu – the navigator shrine. Pulls are "xin xăm": drawing fortune sticks.</summary>
    public sealed class GachaScreen : UiScreen
    {
        static string _lastBannerId;

        List<BannerDef> _banners = new List<BannerDef>();
        BannerDef _banner;
        RectTransform _tabs, _stage;
        readonly Dictionary<string, SkewQuad> _tabBodies = new Dictionary<string, SkewQuad>();

        protected override void Build(object arg)
        {
            Backdrop.Sky(Root);
            Ui.Header(Root, Loc.T("gacha.title"), () => App.Ui.Back());
            CurrencyBar.Create(Root);
            _tabs = Ui.Node("Tabs", Root).At(0f, 1f, 40f, -150f, 420f, 700f, 0f, 1f);
            Ui.VStack(_tabs, 18f);
            _stage = Ui.Node("Stage", Root).Fill(500f, 150f, 40f, 40f);
            LoadBanners();
        }

        async void LoadBanners()
        {
            // Without a connection, show the banners shipped with the game at once; drawing still needs the server.
            if (App.Backend.IsConnected)
            {
                try
                {
                    _banners = await App.Backend.GetBannersAsync();
                }
                catch (BackendException e)
                {
                    if (this == null) return;
                    if (!e.IsConnectivity) Popup.Message(Loc.T("common.error"), e.Friendly);
                    _banners = App.Data.Banners.Active(System.DateTime.UtcNow);
                }
            }
            else
            {
                _banners = App.Data.Banners.Active(System.DateTime.UtcNow);
            }
            if (this == null) return;
            if (!App.Backend.IsConnected)
            {
                var note = Ui.Label(Root, Loc.T("net.offlineGacha"), 26, UiTheme.Bad, TextAnchor.LowerLeft, FontStyle.Bold);
                note.rectTransform.At(0f, 0f, 40f, 40f, 420f, 140f, 0f, 0f);
            }
            foreach (var b in _banners)
            {
                var banner = b;
                var tab = Ui.Button(_tabs, Loc.Pick(b.title), () => Select(banner), ButtonStyle.Secondary, 26);
                tab.Layout(-1f, 110f);
                _tabBodies[b.id] = tab.transform.Find("Body").GetComponent<SkewQuad>();
            }
            BannerDef initial = null;
            foreach (var b in _banners) if (b.id == _lastBannerId) initial = b;
            if (initial == null && _banners.Count > 0) initial = _banners[0];
            if (initial != null) Select(initial);
        }

        void Select(BannerDef banner)
        {
            _banner = banner;
            _lastBannerId = banner.id;
            foreach (var kv in _tabBodies)
            {
                bool on = kv.Key == banner.id;
                kv.Value.SetColors(on ? UiTheme.Hex("#4FB0FF") : Color.white, on ? UiTheme.Primary : UiTheme.PanelAlt);
                kv.Value.transform.parent.GetComponentInChildren<Text>().color = on ? Color.white : UiTheme.InkSoft;
            }
            BuildStage();
        }

        public override void OnProfileChanged()
        {
            if (_banner != null) BuildStage();
        }

        void BuildStage()
        {
            for (int i = _stage.childCount - 1; i >= 0; i--) Destroy(_stage.GetChild(i).gameObject);
            var b = _banner;
            Color color = UiTheme.Hex(string.IsNullOrEmpty(b.color) ? "#2E9BFF" : b.color);

            var card = Ui.Card(_stage, Color.white, "BannerCard");
            card.rectTransform.Fill(0f, 0f, 0f, 150f);
            var wash = Ui.Gradient(card.rectTransform, Color.Lerp(color, Color.white, 0.55f), Color.Lerp(color, Color.white, 0.15f), "Wash");
            wash.rectTransform.Fill(10f, 10f, 10f, 10f);
            var hatch = Ui.Image(card.rectTransform, UiSprites.Hatch, Color.white.WithAlpha(0.5f), "Hatch");
            hatch.type = Image.Type.Tiled;
            hatch.rectTransform.Fill(10f, 10f, 10f, 10f);
            hatch.enabled = !UiTheme.Flat;

            // Featured navigators.
            var featured = new List<NavigatorDef>();
            if (b.featured != null) foreach (var id in b.featured) { var d = App.Data.Navigator(id); if (d != null) featured.Add(d); }
            if (featured.Count > 0)
            {
                var star = NavigatorArt.Standing(card.rectTransform, featured[0], 680f);
                star.At(0.25f, 0f, 0f, 10f, 420f, 680f, 0.5f, 0f);
                for (int i = 1; i < featured.Count && i < 3; i++)
                {
                    var p = NavigatorArt.Portrait(card.rectTransform, featured[i], 170f);
                    p.At(0.5f, 0f, -40f + (i - 1) * 190f, 40f, 170f, 170f, 0.5f, 0f);
                }
            }
            else
            {
                var emblem = Ui.Label(card.rectTransform, "籤", 420, color.WithAlpha(0.35f), TextAnchor.MiddleCenter, FontStyle.Bold);
                emblem.rectTransform.At(0.25f, 0.5f, 0f, 0f, 600f, 600f);
            }

            var text = Ui.Node("Text", card.rectTransform).Region(0.48f, 0f, 1f, 1f);
            text.offsetMin = new Vector2(10f, 30f);
            text.offsetMax = new Vector2(-40f, -40f);
            Ui.VStack(text, 10f, 0, TextAnchor.UpperLeft);
            var typeTag = Ui.Label(text, Loc.T(b.type == "limited" ? "gacha.limited" : "gacha.standard"), 26, color, TextAnchor.MiddleLeft, FontStyle.Bold);
            typeTag.Layout(-1f, 40f);
            var title = Ui.FitLabel(text, Loc.Pick(b.title), 50, UiTheme.Ink, TextAnchor.MiddleLeft);
            title.Layout(-1f, 74f);
            var sub = Ui.Label(text, Loc.Pick(b.subtitle), 26, UiTheme.InkSoft);
            sub.Layout(-1f, 80f);
            if (!string.IsNullOrEmpty(b.endUtc))
            {
                System.DateTime end;
                if (BannerDef.TryParseUtc(b.endUtc, out end))
                    Ui.Label(text, Loc.F("gacha.ends", end.ToLocalTime().ToString(Loc.T("format.date"))), 24, UiTheme.InkSoft).Layout(-1f, 36f);
            }

            var cfg = App.Data.Gacha;
            var pity = App.Profile.gacha.GetPity(b.PityGroup);
            if (cfg.hardPity > 0)
                Ui.Label(text, Loc.F("gacha.pity", Mathf.Max(1, cfg.hardPity - pity.pullsSinceThien)), 25, UiTheme.Ink, TextAnchor.MiddleLeft, FontStyle.Bold).Layout(-1f, 38f);
            if (b.HasFeatured && pity.featuredGuaranteed)
                Ui.Label(text, Loc.T("gacha.guaranteed"), 25, UiTheme.Lacquer, TextAnchor.MiddleLeft, FontStyle.Bold).Layout(-1f, 38f);
            if (b.HasFeatured && cfg.sparkCost > 0)
            {
                int points = App.Profile.gacha.GetSpark(b.id).points;
                Ui.Label(text, Loc.F("gacha.spark", points, cfg.sparkCost), 25, UiTheme.InkSoft).Layout(-1f, 38f);
                if (points >= cfg.sparkCost)
                    Ui.Button(text, Loc.T("gacha.sparkExchange"), ShowSpark, ButtonStyle.Danger, 26).Layout(360f, 72f);
            }

            var rates = Ui.Button(card.rectTransform, Loc.T("gacha.rates"), ShowRates, ButtonStyle.Ghost, 26);
            rates.GetComponent<RectTransform>().At(1f, 1f, -24f, -16f, 260f, 64f, 1f, 1f);

            // Pull buttons.
            var single = Ui.Button(_stage, Loc.T("gacha.pull1"), () => Pull(1), ButtonStyle.Primary, 38);
            single.GetComponent<RectTransform>().At(1f, 0f, -480f, 0f, 420f, 120f, 1f, 0f);
            CostLabel(single.transform, 1);
            var ten = Ui.Button(_stage, Loc.T("gacha.pull10"), () => Pull(10), ButtonStyle.Accent, 38);
            ten.GetComponent<RectTransform>().At(1f, 0f, 0f, 0f, 440f, 120f, 1f, 0f);
            CostLabel(ten.transform, 10);
        }

        void CostLabel(Transform button, int count)
        {
            int tickets = Mathf.Min(App.Profile.tickets, count);
            int jade = (count - tickets) * App.Data.Gacha.costPerPull;
            string cost = tickets > 0 ? "✦" + tickets + (jade > 0 ? "  ◆" + jade : string.Empty) : "◆" + jade;
            var label = button.GetComponentInChildren<Text>();
            label.rectTransform.Fill(16f, 4f, 16f, 40f);
            var c = Ui.Label(button, cost, 24, label.color, TextAnchor.LowerCenter, FontStyle.Bold, "Cost");
            c.rectTransform.Fill(16f, 70f, 16f, 10f);
        }

        async void Pull(int count)
        {
            PullResponse response = null;
            bool ok = await App.Ui.Busy(Loc.T("gacha.drawing"), async () => response = await App.Backend.PullAsync(_banner.id, count));
            if (!ok || response == null) return;
            App.SetProfile(response.profile);
            App.Ui.Show<GachaRevealScreen>(new GachaRevealScreen.Args { results = response.results, bannerId = _banner.id, count = count });
        }

        void ShowSpark()
        {
            var p = Popup.Open(Loc.T("gacha.sparkExchange"), 1100f, 620f);
            var row = Ui.Node("Choices", p.Body).Fill(0f, 40f, 0f, 0f);
            Ui.HStack(row, 30f, 0, TextAnchor.MiddleCenter);
            foreach (var id in _banner.featured)
            {
                var def = App.Data.Navigator(id);
                if (def == null) continue;
                var cell = Ui.Node("Choice", row);
                cell.Layout(230f, 330f);
                var portrait = NavigatorArt.Portrait(cell, def, 220f);
                portrait.At(0.5f, 1f, 0f, 0f, 220f, 220f, 0.5f, 1f);
                NavigatorArt.NamePlate(cell, def, 230f);
                cell.GetChild(cell.childCount - 1).GetComponent<RectTransform>().At(0.5f, 0f, 0f, 0f, 230f, 90f, 0.5f, 0f);
                var hit = Ui.Image(cell, UiSprites.White, new Color(0, 0, 0, 0), "Hit");
                hit.rectTransform.Fill();
                hit.raycastTarget = true;
                Ui.AddClick(hit.gameObject, async () =>
                {
                    p.Close();
                    PullResponse response = null;
                    bool ok = await App.Ui.Busy(Loc.T("common.working"), async () => response = await App.Backend.SparkAsync(_banner.id, def.id));
                    if (!ok || response == null) return;
                    App.SetProfile(response.profile);
                    App.Ui.Show<GachaRevealScreen>(new GachaRevealScreen.Args { results = response.results, bannerId = _banner.id, count = 0 });
                });
            }
        }

        /// <summary>Published odds per navigator (loot-box disclosure, required on Google Play).</summary>
        void ShowRates()
        {
            var cfg = App.Data.Gacha;
            var engine = new GachaEngine(cfg, App.Data.Navigators);
            var p = Popup.Open(Loc.T("gacha.rates"), 1300f, 900f);
            Ui.ScrollList(p.Body, out var content, 6f, 4).GetComponent<RectTransform>().Fill();
            var sb = new StringBuilder();
            sb.AppendLine(Loc.F("gacha.ratesBase", Format.Percent((float)cfg.rates.thien), Format.Percent((float)cfg.rates.dia), Format.Percent((float)(1.0 - cfg.rates.thien - cfg.rates.dia))));
            if (cfg.softPityStart > 0) sb.AppendLine(Loc.F("gacha.ratesSoft", cfg.softPityStart, Format.Percent((float)cfg.softPityStep)));
            if (cfg.hardPity > 0) sb.AppendLine(Loc.F("gacha.ratesHard", cfg.hardPity));
            if (_banner.HasFeatured) sb.AppendLine(Loc.F("gacha.ratesFeatured", Format.Percent((float)cfg.featuredShare)));
            sb.AppendLine(Loc.T("gacha.ratesTen"));
            if (cfg.sparkCost > 0 && _banner.HasFeatured) sb.AppendLine(Loc.F("gacha.ratesSpark", cfg.sparkCost));
            var rules = Ui.Label(content, sb.ToString(), 24, UiTheme.Ink);
            rules.Layout(-1f, 230f);
            foreach (var line in engine.DescribeRates(_banner))
            {
                var def = App.Data.Navigator(line.navigatorId);
                int rarity = (int)line.rarity;
                string text = UiTheme.RarityStars(rarity) + "  " + Loc.Pick(def.name) +
                              (line.featured ? "  (" + Loc.T("gacha.featured") + ")" : string.Empty) +
                              "  —  " + (line.probability * 100.0).ToString("0.000") + "%";
                var row = Ui.Label(content, text, 25, rarity == 1 ? UiTheme.Ink : UiTheme.RarityColor(rarity), TextAnchor.MiddleLeft,
                    line.featured ? FontStyle.Bold : FontStyle.Normal);
                row.Layout(-1f, 40f);
            }
        }
    }

    /// <summary>The xin xăm ceremony: shake the bamboo cylinder, a stick flies out, then the cards turn over.</summary>
    public sealed class GachaRevealScreen : UiScreen
    {
        public sealed class Args
        {
            public PullResult[] results;
            public string bannerId;
            public int count;
        }

        Args _args;
        bool _skip;
        RectTransform _cylinder, _cards;
        Text _hint;

        protected override void Build(object arg)
        {
            _args = (Args)arg;
            Backdrop.Shrine(Root);
            var tap = Ui.Image(Root, UiSprites.White, new Color(0, 0, 0, 0), "TapCatcher");
            tap.rectTransform.Fill();
            tap.raycastTarget = true;
            Ui.AddClick(tap.gameObject, () => _skip = true);

            _cylinder = BuildCylinder(Root);
            _cards = Ui.Node("Cards", Root).Fill(80f, 80f, 80f, 200f);
            _hint = Ui.Label(Root, Loc.T("gacha.tapToSkip"), 26, Color.white.WithAlpha(0.7f), TextAnchor.MiddleCenter);
            _hint.rectTransform.At(0.5f, 0f, 0f, 60f, 900f, 50f);
            StartCoroutine(Ceremony());
        }

        public override bool OnBack()
        {
            _skip = true;
            return true;
        }

        static RectTransform BuildCylinder(RectTransform parent)
        {
            var root = Ui.Node("Cylinder", parent).At(0.5f, 0.5f, 0f, -40f, 300f, 520f, 0.5f, 0f);
            for (int i = 0; i < 7; i++)
            {
                var stick = Ui.Image(root, UiSprites.RoundedSmall, UiTheme.Hex("#E9C98B"), "Stick" + i);
                stick.rectTransform.At(0.5f, 0f, -90f + i * 30f, 300f, 22f, 260f, 0.5f, 0f);
                stick.rectTransform.localRotation = Quaternion.Euler(0f, 0f, (i - 3) * 3f);
            }
            var body = Ui.Image(root, UiSprites.Rounded, UiTheme.Hex("#8C4A2F"), "Body");
            body.rectTransform.At(0.5f, 0f, 0f, 0f, 280f, 400f, 0.5f, 0f);
            for (int i = 0; i < 3; i++)
            {
                var band = Ui.Image(body.rectTransform, UiSprites.White, UiTheme.Hex("#5E2E1C"), "Band" + i);
                band.rectTransform.At(0.5f, 0f, 0f, 60f + i * 120f, 280f, 14f, 0.5f, 0.5f);
            }
            var label = Ui.Image(body.rectTransform, UiSprites.RoundedSmall, UiTheme.Lacquer, "Label");
            label.rectTransform.At(0.5f, 0.5f, 0f, 0f, 120f, 200f);
            var glyph = Ui.Label(label.rectTransform, "靈", 96, UiTheme.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            glyph.rectTransform.Fill();
            return root;
        }

        IEnumerator Ceremony()
        {
            int best = 1;
            foreach (var r in _args.results) best = Mathf.Max(best, r.rarity);
            Color bestColor = UiTheme.RarityColor(best);

            // 1. Shake.
            App.Audio.Rattle();
            for (float t = 0f; t < 1.5f && !_skip; t += Time.unscaledDeltaTime)
            {
                float amp = Mathf.Lerp(4f, 16f, t / 1.5f);
                _cylinder.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 38f) * amp);
                yield return null;
            }
            _cylinder.localRotation = Quaternion.identity;

            // 2. The stick of fate flies out, coloured by the best rarity.
            var stick = Ui.Image(Root, UiSprites.RoundedSmall, bestColor, "FateStick");
            stick.rectTransform.At(0.5f, 0.5f, 0f, 300f, 34f, 300f);
            var glow = Ui.Image(stick.rectTransform, UiSprites.Glow, bestColor.WithAlpha(0.8f), "Glow");
            glow.rectTransform.Fill(-140f, -140f, -140f, -140f);
            App.Audio.Bell(best);
            for (float t = 0f; t < 0.7f && !_skip; t += Time.unscaledDeltaTime)
            {
                float k = t / 0.7f;
                stick.rectTransform.anchoredPosition = new Vector2(0f, Mathf.Lerp(300f, 700f, 1f - (1f - k) * (1f - k)));
                stick.rectTransform.localRotation = Quaternion.Euler(0f, 0f, k * 540f);
                yield return null;
            }
            var flash = Ui.Image(Root, UiSprites.White, Color.Lerp(bestColor, Color.white, 0.6f), "Flash");
            flash.rectTransform.Fill(-200f, -200f, -200f, -200f);
            for (float t = 0f; t < 0.35f; t += Time.unscaledDeltaTime)
            {
                flash.color = flash.color.WithAlpha(1f - t / 0.35f);
                yield return null;
            }
            Destroy(flash.gameObject);
            Destroy(stick.gameObject);
            _cylinder.gameObject.SetActive(false);
            _skip = false;

            // 3. Cards turn over one by one.
            var results = _args.results;
            int columns = results.Length > 5 ? 5 : results.Length;
            int rows = Mathf.CeilToInt(results.Length / (float)columns);
            float cell = results.Length > 1 ? 300f : 420f;
            NavigatorDef highlight = null;
            for (int i = 0; i < results.Length; i++)
            {
                var r = results[i];
                var def = App.Data.Navigator(r.navigatorId);
                int col = i % columns, row = i / columns;
                float x = (col - (columns - 1) * 0.5f) * (cell + 30f);
                float y = ((rows - 1) * 0.5f - row) * (cell + 120f) + 60f;
                var card = Ui.Node("Result" + i, _cards).At(0.5f, 0.5f, x, y, cell, cell + 90f);
                var aura = Ui.Image(card, UiSprites.Glow, UiTheme.RarityColor(r.rarity).WithAlpha(r.rarity >= 3 ? 0.9f : 0.45f), "Aura");
                aura.rectTransform.Fill(-60f, -60f, -60f, -60f);
                var portrait = NavigatorArt.Portrait(card, def, cell);
                portrait.At(0.5f, 1f, 0f, 0f, cell, cell, 0.5f, 1f);
                var name = Ui.FitLabel(card, UiTheme.RarityStars(r.rarity) + " " + Loc.Pick(def.name), 28, Color.white, TextAnchor.MiddleCenter);
                name.rectTransform.At(0.5f, 0f, 0f, 30f, cell + 20f, 50f, 0.5f, 0f);
                string tagText = r.isNew ? Loc.T("gacha.new") : Loc.F("gacha.fragments", r.fragments);
                var tag = Ui.Skew(card, r.isNew ? UiTheme.Lacquer : UiTheme.Primary, r.isNew ? UiTheme.Lacquer : UiTheme.PrimaryDark, 12f, "Tag");
                tag.rectTransform.At(0f, 1f, -6f, 10f, 150f, 44f, 0f, 1f);
                Ui.FitLabel(tag.rectTransform, tagText, 24, Color.white, TextAnchor.MiddleCenter).rectTransform.Fill(8f, 2f, 8f, 2f);

                if (r.rarity >= 3) App.Audio.Bell(3);
                else App.Audio.Tap();
                if (r.rarity >= 3 && r.isNew && highlight == null) highlight = def;

                float duration = _skip ? 0.01f : (r.rarity >= 3 ? 0.45f : 0.18f);
                for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
                {
                    float k = t / duration;
                    card.localScale = new Vector3(Mathf.Abs(Mathf.Cos((1f - k) * Mathf.PI * 0.5f)), 1f, 1f) * Mathf.Lerp(1.15f, 1f, k);
                    yield return null;
                }
                card.localScale = Vector3.one;
                if (!_skip && r.rarity >= 3) yield return new WaitForSecondsRealtime(0.35f);
            }
            _hint.gameObject.SetActive(false);

            if (highlight != null)
            {
                var lines = new List<VoiceLine>(highlight.LinesFor(VoiceTriggers.Obtained, 1));
                if (lines.Count > 0)
                {
                    var bubble = Ui.Node("Obtained", Root).At(0.5f, 1f, 0f, -40f, 1300f, 120f, 0.5f, 1f);
                    Ui.Image(bubble, UiSprites.Rounded, Color.white.WithAlpha(0.95f), "Bg").rectTransform.Fill();
                    Ui.Label(bubble, Loc.Pick(highlight.name) + ": " + Loc.Pick(lines[0].text), 28, UiTheme.Ink, TextAnchor.MiddleCenter).rectTransform.Fill(30f, 10f, 30f, 10f);
                    var clip = NavigatorArt.Voice(highlight.id, lines[0].id);
                    if (clip != null) App.Audio.Voice(clip);
                }
            }

            // 4. Actions.
            var done = Ui.Button(Root, Loc.T("common.done"), () => App.Ui.Back(), ButtonStyle.Secondary, 34);
            done.GetComponent<RectTransform>().At(0.5f, 0f, _args.count > 0 ? -250f : 0f, 50f, 420f, 110f, 0.5f, 0f);
            if (_args.count > 0)
            {
                var again = Ui.Button(Root, _args.count == 10 ? Loc.T("gacha.pull10") : Loc.T("gacha.pull1"), PullAgain, ButtonStyle.Accent, 34);
                again.GetComponent<RectTransform>().At(0.5f, 0f, 250f, 50f, 420f, 110f, 0.5f, 0f);
            }
        }

        async void PullAgain()
        {
            PullResponse response = null;
            bool ok = await App.Ui.Busy(Loc.T("gacha.drawing"), async () => response = await App.Backend.PullAsync(_args.bannerId, _args.count));
            if (!ok || response == null) return;
            App.SetProfile(response.profile);
            App.Ui.Show<GachaRevealScreen>(new Args { results = response.results, bannerId = _args.bannerId, count = _args.count }, false);
        }
    }
}

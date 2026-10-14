using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VMS.Core;
using VMS.Race;

namespace VMS.UI
{
    /// <summary>
    /// Thế Giới – the world atlas: Vạn Lạc and the realms beyond the Five Gates, with each realm's roads (race
    /// themes), the navigators who come from it and a codex of its places, architecture, artifacts and myths.
    /// </summary>
    public sealed class WorldScreen : UiScreen
    {
        static string _lastRealm;

        RectTransform _tabs, _detail;
        string _selected;

        protected override void Build(object arg)
        {
            Backdrop.Sky(Root);
            Ui.Header(Root, Loc.T("world.title"), () => App.Ui.Back());
            _tabs = Ui.Node("Tabs", Root).At(0f, 1f, 40f, -150f, 420f, 880f, 0f, 1f);
            Ui.VStack(_tabs, 14f);
            _detail = Ui.Card(Root, UiTheme.Panel.WithAlpha(0.96f), "Detail").rectTransform;
            _detail.Fill(490f, 150f, 40f, 40f);
            var realms = App.Data.World.Sorted();
            if (realms.Count == 0) return;
            _selected = arg as string ?? _lastRealm;
            if (App.Data.World.Find(_selected) == null) _selected = realms[0].id;
            Rebuild();
        }

        void Rebuild()
        {
            _lastRealm = _selected;
            for (int i = _tabs.childCount - 1; i >= 0; i--) Destroy(_tabs.GetChild(i).gameObject);
            foreach (var realm in App.Data.World.Sorted())
            {
                var r = realm;
                bool on = r.id == _selected;
                Color c = CosmeticArt.Hex(r.color, UiTheme.Primary);
                var tab = Ui.Button(_tabs, Loc.Pick(r.name), () =>
                {
                    _selected = r.id;
                    Rebuild();
                }, ButtonStyle.Secondary, 28);
                tab.Layout(-1f, 118f);
                tab.transform.Find("Body").GetComponent<SkewQuad>().SetColors(on ? Color.Lerp(c, Color.white, 0.2f) : Color.white, on ? c : UiTheme.PanelAlt);
                var label = tab.GetComponentInChildren<Text>();
                label.color = on ? Color.white : UiTheme.Ink;
                label.rectTransform.Fill(96f, 4f, 16f, 4f);
                label.alignment = TextAnchor.MiddleLeft;
                var glyph = Ui.Label(tab.transform, r.glyph, 46, on ? Color.white : c, TextAnchor.MiddleCenter, FontStyle.Bold, "Glyph");
                glyph.rectTransform.At(0f, 0.5f, 22f, 0f, 70f, 70f, 0f, 0.5f);
            }
            for (int i = _detail.childCount - 1; i >= 0; i--)
                if (_detail.GetChild(i).name != "Shadow") Destroy(_detail.GetChild(i).gameObject);
            var def = App.Data.World.Find(_selected);
            if (def != null) BuildDetail(def);
        }

        void BuildDetail(RealmDef realm)
        {
            Color c = CosmeticArt.Hex(realm.color, UiTheme.Primary);
            var emblem = Ui.Label(_detail, realm.glyph, 170, c.WithAlpha(0.16f), TextAnchor.MiddleCenter, FontStyle.Bold, "Emblem");
            emblem.rectTransform.At(1f, 1f, -30f, -6f, 240f, 220f, 1f, 1f);
            // Pale realm colours (snow, cloud) get a deeper banner so the white title stays readable.
            var banner = Ui.Skew(_detail, Color.Lerp(c, Color.white, 0.15f), Color.Lerp(c, UiTheme.Ink, 0.3f), 40f, "Banner");
            banner.rectTransform.At(0f, 1f, 30f, -24f, 900f, 116f, 0f, 1f);
            Ui.FitLabel(banner.rectTransform, Loc.Pick(realm.name), 46, Color.white, TextAnchor.UpperLeft).rectTransform.Fill(40f, 8f, 30f, 46f);
            Ui.FitLabel(banner.rectTransform, Loc.Pick(realm.title), 26, Color.white, TextAnchor.LowerLeft, FontStyle.Normal).rectTransform.Fill(40f, 62f, 30f, 10f);

            var area = Ui.Node("Body", _detail).Fill(24f, 160f, 24f, 20f);
            var scroll = Ui.ScrollList(area, out var content, 18f, 12);
            scroll.GetComponent<RectTransform>().Fill();

            Ui.Label(content, Loc.Pick(realm.desc), 28, UiTheme.Ink, TextAnchor.UpperLeft);

            Section(content, Loc.T("world.roads"), c);
            var roads = Ui.Node("Roads", content);
            roads.Layout(-1f, 96f);
            var row = Ui.HStack(roads, 14f);
            row.childForceExpandWidth = true;
            row.childForceExpandHeight = true;
            foreach (var theme in RaceTheme.OfRealm(realm.id)) RoadButton(roads, theme);

            Section(content, Loc.T("world.navigators"), c);
            var natives = new List<NavigatorDef>();
            foreach (var n in App.Data.AllNavigators()) if (GameData.RealmOf(n) == realm.id) natives.Add(n);
            if (natives.Count == 0)
            {
                Ui.Label(content, Loc.T("world.noNavigators"), 26, UiTheme.InkSoft).Layout(-1f, 50f);
            }
            else
            {
                var grid = Ui.Node("Navigators", content);
                const float cell = 190f;
                int perRow = 7;
                int rows = (natives.Count + perRow - 1) / perRow;
                grid.Layout(-1f, rows * (cell + 46f));
                var layout = grid.gameObject.AddComponent<GridLayoutGroup>();
                layout.cellSize = new Vector2(cell, cell + 36f);
                layout.spacing = new Vector2(10f, 10f);
                foreach (var n in natives) NavigatorCell(grid, n, cell);
            }

            Section(content, Loc.T("world.codex"), c);
            foreach (var entry in realm.lore) LoreCard(content, entry, c);
        }

        static void Section(RectTransform content, string title, Color color)
        {
            var t = Ui.Label(content, title, 32, Color.Lerp(color, UiTheme.Ink, 0.35f), TextAnchor.LowerLeft, FontStyle.Bold);
            t.Layout(-1f, 60f);
        }

        void RoadButton(RectTransform parent, RaceTheme theme)
        {
            var b = Ui.Button(parent, Loc.T(theme.nameKey), () =>
            {
                App.Settings.Theme = theme.id;
                App.ApplySettings();
                App.Ui.Show<SongSelectScreen>();
            }, ButtonStyle.Primary, 26);
            b.transform.Find("Body").GetComponent<SkewQuad>().SetColors(Color.Lerp(theme.skyHorizon, theme.skyTop, 0.35f), theme.skyTop);
            var label = b.GetComponentInChildren<Text>();
            label.color = Color.white;
            var stripe = Ui.Skew(b.transform, theme.roadGrid, theme.roadGrid, 8f, "Road");
            stripe.rectTransform.anchorMin = new Vector2(0f, 0f);
            stripe.rectTransform.anchorMax = new Vector2(1f, 0f);
            stripe.rectTransform.offsetMin = new Vector2(10f, 0f);
            stripe.rectTransform.offsetMax = new Vector2(-10f, 8f);
            if (App.Settings.Theme == theme.id)
            {
                var mark = Ui.Label(b.transform, "✓", 30, theme.roadBeat, TextAnchor.UpperRight, FontStyle.Bold, "Chosen");
                mark.rectTransform.Fill(0f, 4f, 22f, 0f);
            }
        }

        void NavigatorCell(RectTransform grid, NavigatorDef def, float size)
        {
            bool owned = App.Profile != null && App.Profile.Owns(def.id);
            var holder = Ui.Node("Navigator " + def.id, grid);
            var portrait = NavigatorArt.Portrait(holder, def, size, !owned);
            portrait.At(0.5f, 1f, 0f, 0f, size, size, 0.5f, 1f);
            var name = Ui.FitLabel(holder, Loc.Pick(def.name), 22, owned ? UiTheme.Ink : UiTheme.InkSoft, TextAnchor.MiddleCenter);
            name.rectTransform.At(0.5f, 0f, 0f, 0f, size + 10f, 34f, 0.5f, 0f);
            var hit = Ui.Image(holder, UiSprites.White, new Color(0f, 0f, 0f, 0f), "Hit");
            hit.rectTransform.Fill();
            hit.raycastTarget = true;
            Ui.AddClick(hit.gameObject, () => App.Ui.Show<NavigatorDetailScreen>(def));
        }

        static void LoreCard(RectTransform content, LoreEntry entry, Color color)
        {
            var card = Ui.Card(content, UiTheme.PanelAlt, "Lore " + entry.id, false).rectTransform;
            var stack = Ui.VStack(card, 6f, 18, TextAnchor.UpperLeft);
            stack.padding = new RectOffset(24, 24, 16, 18);
            var head = Ui.Node("Head", card);
            head.Layout(-1f, 40f);
            Color tagColor = Color.Lerp(color, UiTheme.Ink, 0.35f);
            var tag = Ui.Skew(head, tagColor, tagColor, 12f, "Kind");
            tag.rectTransform.At(0f, 0.5f, 8f, 0f, 180f, 34f, 0f, 0.5f);
            Ui.FitLabel(tag.rectTransform, Loc.T("lore." + (string.IsNullOrEmpty(entry.kind) ? "myth" : entry.kind)), 20, Color.white, TextAnchor.MiddleCenter)
                .rectTransform.Fill(8f, 2f, 8f, 2f);
            var name = Ui.Label(head, Loc.Pick(entry.name), 30, UiTheme.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            name.rectTransform.Fill(206f, 0f, 0f, 0f);
            Ui.Label(card, Loc.Pick(entry.text), 26, UiTheme.InkSoft, TextAnchor.UpperLeft);
        }
    }

    /// <summary>Road (race theme) chooser grouped by realm; "auto" lets each song keep its own road.</summary>
    public static class RoadPicker
    {
        public static string Describe(string themeId)
        {
            if (string.IsNullOrEmpty(themeId) || themeId == "auto") return Loc.T("theme.auto");
            foreach (var t in RaceTheme.All)
                if (t.id == themeId)
                {
                    var realm = GameApp.Instance != null ? GameApp.Instance.Data.World.Find(t.realm) : null;
                    return realm != null ? Loc.T(t.nameKey) + " · " + Loc.Pick(realm.name) : Loc.T(t.nameKey);
                }
            return Loc.T("theme.auto");
        }

        public static void Show(string current, System.Action<string> onPick)
        {
            var p = Popup.Open(Loc.T("songs.pickRoad"), 1400f, 900f);
            var scroll = Ui.ScrollList(p.Body, out var content, 12f, 6);
            scroll.GetComponent<RectTransform>().Fill();
            Option(content, "auto", Loc.T("theme.auto"), UiTheme.Primary, UiTheme.PrimaryDark, current, id => { p.Close(); onPick(id); });
            foreach (var realm in GameApp.Instance.Data.World.Sorted())
            {
                var themes = RaceTheme.OfRealm(realm.id);
                if (themes.Count == 0) continue;
                Color c = CosmeticArt.Hex(realm.color, UiTheme.Primary);
                var head = Ui.Label(content, realm.glyph + "  " + Loc.Pick(realm.name) + "  ·  " + Loc.Pick(realm.title), 28,
                    Color.Lerp(c, UiTheme.Ink, 0.35f), TextAnchor.LowerLeft, FontStyle.Bold);
                head.Layout(-1f, 54f);
                var row = Ui.Node("Row " + realm.id, content);
                row.Layout(-1f, 80f);
                var h = Ui.HStack(row, 12f);
                h.childForceExpandWidth = true;
                h.childForceExpandHeight = true;
                foreach (var t in themes)
                    Option(row, t.id, Loc.T(t.nameKey), Color.Lerp(t.skyHorizon, t.skyTop, 0.35f), t.skyTop, current, id => { p.Close(); onPick(id); });
            }
        }

        static void Option(RectTransform parent, string id, string label, Color top, Color bottom, string current, System.Action<string> pick)
        {
            var b = Ui.Button(parent, (id == current ? "✓ " : string.Empty) + label, () => pick(id), ButtonStyle.Primary, 26);
            b.transform.Find("Body").GetComponent<SkewQuad>().SetColors(top, bottom);
            b.GetComponentInChildren<Text>().color = Color.white;
            if (parent.GetComponent<HorizontalLayoutGroup>() == null) b.Layout(-1f, 72f);
        }
    }
}

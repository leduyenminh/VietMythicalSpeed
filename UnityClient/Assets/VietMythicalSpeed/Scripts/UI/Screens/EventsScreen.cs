using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VMS.Backend;
using VMS.Core;

namespace VMS.UI
{
    /// <summary>Lễ Hội – time-limited events (login calendars, race and score milestones) and their rewards.</summary>
    public sealed class EventsScreen : UiScreen
    {
        static string _lastEvent;

        RectTransform _tabs, _detail;
        string _selected;

        protected override void Build(object arg)
        {
            Backdrop.Sky(Root);
            Ui.Header(Root, Loc.T("events.title"), () => App.Ui.Back());
            CurrencyBar.Create(Root);
            _tabs = Ui.Node("Tabs", Root).At(0f, 1f, 40f, -150f, 420f, 880f, 0f, 1f);
            Ui.VStack(_tabs, 14f);
            _detail = Ui.Card(Root, UiTheme.Panel.WithAlpha(0.96f), "Detail").rectTransform;
            _detail.Fill(490f, 150f, 40f, 40f);
            var events = App.Events;
            if (events.Count == 0)
            {
                Ui.Label(_detail, Loc.T("events.none"), 30, UiTheme.InkSoft, TextAnchor.MiddleCenter).rectTransform.Fill();
                return;
            }
            _selected = arg as string ?? _lastEvent;
            if (events.Find(e => e.id == _selected) == null) _selected = events[0].id;
            Rebuild();
        }

        public override void OnProfileChanged()
        {
            if (_detail != null && _selected != null) Rebuild();
        }

        void Rebuild()
        {
            _lastEvent = _selected;
            for (int i = _tabs.childCount - 1; i >= 0; i--) Destroy(_tabs.GetChild(i).gameObject);
            foreach (var e in App.Events)
            {
                var ev = e;
                bool on = e.id == _selected;
                Color c = CosmeticArt.Hex(e.color, UiTheme.Primary);
                var tab = Ui.Button(_tabs, Loc.Pick(e.title), () =>
                {
                    _selected = ev.id;
                    Rebuild();
                }, ButtonStyle.Secondary, 26);
                tab.Layout(-1f, 100f);
                tab.transform.Find("Body").GetComponent<SkewQuad>().SetColors(on ? Color.Lerp(c, Color.white, 0.2f) : Color.white, on ? c : UiTheme.PanelAlt);
                tab.GetComponentInChildren<Text>().color = on ? Color.white : UiTheme.Ink;
                if (Claimable(e) > 0)
                {
                    var dot = Ui.Image(tab.transform, UiSprites.Circle, UiTheme.Lacquer, "Badge");
                    dot.rectTransform.At(1f, 1f, -8f, -4f, 26f, 26f, 1f, 1f);
                }
            }
            for (int i = _detail.childCount - 1; i >= 0; i--)
                if (_detail.GetChild(i).name != "Shadow") Destroy(_detail.GetChild(i).gameObject);
            var def = App.Events.Find(x => x.id == _selected);
            if (def != null) BuildDetail(def);
        }

        int Claimable(EventDef e)
        {
            var p = App.Profile != null ? App.Profile.events.Find(x => x.id == e.id) : null;
            if (p == null) return 0;
            int n = 0;
            for (int i = 0; i < e.milestones.Count; i++)
                if (p.value >= e.milestones[i].target && !p.claimed.Contains(i)) n++;
            return n;
        }

        void BuildDetail(EventDef e)
        {
            Color c = CosmeticArt.Hex(e.color, UiTheme.Primary);
            var banner = Ui.Skew(_detail, Color.Lerp(c, Color.white, 0.25f), c, 40f, "Banner");
            banner.rectTransform.At(0f, 1f, 30f, -24f, 860f, 100f, 0f, 1f);
            Ui.FitLabel(banner.rectTransform, Loc.Pick(e.title), 44, Color.white, TextAnchor.MiddleLeft).rectTransform.Fill(40f, 6f, 30f, 6f);
            var time = Ui.Label(_detail, TimeLeft(e), 24, UiTheme.InkSoft, TextAnchor.MiddleRight, FontStyle.Bold);
            time.rectTransform.At(1f, 1f, -40f, -50f, 480f, 40f, 1f, 1f);
            var desc = Ui.Label(_detail, Loc.Pick(e.desc), 26, UiTheme.Ink, TextAnchor.UpperLeft);
            desc.rectTransform.At(0f, 1f, 40f, -140f, 1300f, 70f, 0f, 1f);

            var progress = App.Profile != null ? App.Profile.events.Find(x => x.id == e.id) : null;
            long value = progress != null ? progress.value : 0;
            var listArea = Ui.Node("Milestones", _detail).Fill(24f, 220f, 24f, 20f);
            var scroll = Ui.ScrollList(listArea, out var content, 12f, 4);
            scroll.GetComponent<RectTransform>().Fill();
            for (int i = 0; i < e.milestones.Count; i++)
            {
                int index = i;
                var m = e.milestones[i];
                bool reached = value >= m.target;
                bool claimed = progress != null && progress.claimed.Contains(i);
                var row = Ui.Card(content, claimed ? UiTheme.PanelAlt : Color.white, "Milestone " + i).rectTransform;
                row.Layout(-1f, 120f);
                var goal = Ui.FitLabel(row, Goal(e, m.target), 28, reached ? c : UiTheme.Ink, TextAnchor.MiddleLeft);
                goal.rectTransform.At(0f, 1f, 24f, -14f, 300f, 40f, 0f, 1f);
                // Progress bar.
                var track = Ui.Image(row, UiSprites.RoundedSmall, UiTheme.PanelAlt, "Track");
                track.rectTransform.At(0f, 0f, 24f, 24f, 210f, 22f, 0f, 0f);
                float k = Mathf.Clamp01(m.target > 0 ? (float)value / m.target : 1f);
                var fill = Ui.Image(track.rectTransform, UiSprites.RoundedSmall, c, "Fill");
                fill.rectTransform.anchorMin = Vector2.zero;
                fill.rectTransform.anchorMax = new Vector2(Mathf.Max(0.06f, k), 1f);
                fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
                // The count sits beside the bar, so long goal texts (German, Spanish) never collide with it.
                var count = Ui.FitLabel(row, Progress(e, value, m.target), 20, UiTheme.InkSoft, TextAnchor.MiddleLeft, FontStyle.Normal);
                count.rectTransform.At(0f, 0f, 244f, 18f, 96f, 34f, 0f, 0f);
                var gifts = Ui.Node("Rewards", row).Fill(350f, 20f, 280f, 20f);
                Ui.HStack(gifts, 12f, 0, TextAnchor.MiddleLeft);
                RewardView.Add(gifts, m.rewards, 60f);
                Button b;
                if (claimed)
                {
                    b = Ui.Button(row, Loc.T("events.claimed"), null, ButtonStyle.Secondary, 24);
                    b.interactable = false;
                }
                else if (reached)
                {
                    b = Ui.Button(row, Loc.T("events.claim"), () => Claim(e.id, index), ButtonStyle.Accent, 26);
                }
                else
                {
                    b = Ui.Button(row, Loc.T("events.locked"), null, ButtonStyle.Secondary, 24);
                    b.interactable = false;
                }
                b.GetComponent<RectTransform>().At(1f, 0.5f, -20f, 0f, 230f, 76f, 1f, 0.5f);
            }
        }

        static string Goal(EventDef e, long target)
        {
            switch (e.type)
            {
                case EventRules.Login: return Loc.F("events.goalLogin", target);
                case EventRules.Races: return Loc.F("events.goalRaces", target);
                default: return Loc.F("events.goalScore", target.ToString("N0"));
            }
        }

        static string Progress(EventDef e, long value, long target) =>
            Math.Min(value, target).ToString("N0") + " / " + target.ToString("N0");

        static string TimeLeft(EventDef e)
        {
            if (!BannerDef.TryParseUtc(e.endUtc, out var end)) return string.Empty;
            var left = end - DateTime.UtcNow;
            if (left.TotalSeconds <= 0) return Loc.T("events.ended");
            return left.TotalDays >= 1 ? Loc.F("events.daysLeft", (int)left.TotalDays) : Loc.F("events.hoursLeft", Math.Max(1, (int)left.TotalHours));
        }

        async void Claim(string eventId, int index)
        {
            RewardsResponse r = null;
            if (!await App.Ui.Busy(Loc.T("common.working"), async () => r = await App.Backend.ClaimEventAsync(eventId, index))) return;
            App.SetProfile(r.profile);
            App.Ui.Toast(Loc.F("mail.received", RewardView.Summary(r.rewards)), 4f);
        }
    }
}

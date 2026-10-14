using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VMS.Backend;
using VMS.Core;

namespace VMS.UI
{
    /// <summary>Hộp Thư – system mail with gifts (welcome, compensation, events). Gifts are claimed here.</summary>
    public sealed class MailScreen : UiScreen
    {
        RectTransform _listArea, _detail;
        List<MailItem> _mail = new List<MailItem>();
        long _selected;
        Text _status;

        protected override void Build(object arg)
        {
            Backdrop.Sky(Root);
            Ui.Header(Root, Loc.T("mail.title"), () => App.Ui.Back());
            CurrencyBar.Create(Root);
            var left = Ui.Card(Root, UiTheme.Panel.WithAlpha(0.95f), "List").rectTransform;
            left.Fill(40f, 150f, 1160f, 40f);
            _listArea = Ui.Node("Items", left).Fill(14f, 14f, 14f, 120f);
            var claimAll = Ui.Button(left, Loc.T("mail.claimAll"), ClaimAll, ButtonStyle.Accent, 28);
            claimAll.GetComponent<RectTransform>().At(0.5f, 0f, 0f, 20f, 660f, 84f, 0.5f, 0f);
            _detail = Ui.Card(Root, Color.white.WithAlpha(0.97f), "Detail").rectTransform;
            _detail.Fill(800f, 150f, 40f, 40f);
            _status = Ui.Label(left, Loc.T("common.working"), 26, UiTheme.InkSoft, TextAnchor.MiddleCenter);
            _status.rectTransform.Fill(20f, 20f, 20f, 140f);
            Load();
        }

        async void Load()
        {
            try
            {
                var list = await App.Backend.GetMailAsync();
                if (this == null) return;
                _mail = list.mail ?? new List<MailItem>();
                _status.text = _mail.Count == 0 ? Loc.T("mail.empty") : string.Empty;
            }
            catch (BackendException e)
            {
                if (this == null) return;
                _status.text = e.IsConnectivity ? Loc.T("mail.offline") : e.Friendly;
                _mail = new List<MailItem>();
            }
            if (_selected == 0 && _mail.Count > 0) _selected = _mail[0].id;
            Rebuild();
            if (_selected != 0) Select(_selected);
            await App.RefreshMailCountsAsync();
        }

        void Rebuild()
        {
            for (int i = _listArea.childCount - 1; i >= 0; i--) Destroy(_listArea.GetChild(i).gameObject);
            var scroll = Ui.ScrollList(_listArea, out var content, 10f, 4);
            scroll.GetComponent<RectTransform>().Fill();
            foreach (var m in _mail)
            {
                var mail = m;
                bool selected = m.id == _selected;
                var row = Ui.Card(content, selected ? UiTheme.Hex("#E3F1FF") : Color.white, "Mail " + m.id).rectTransform;
                row.Layout(-1f, 116f);
                if (!m.read)
                {
                    var dot = Ui.Image(row, UiSprites.Circle, UiTheme.Lacquer, "Unread");
                    dot.rectTransform.At(0f, 1f, 14f, -16f, 18f, 18f, 0f, 1f);
                }
                var icon = Ui.Image(row, UiSprites.Circle, m.HasAttachments && !m.claimed ? CurrencyBar.GoldColor : UiTheme.PanelAlt, "Icon");
                icon.rectTransform.At(0f, 0.5f, 40f, 0f, 70f, 70f, 0f, 0.5f);
                Ui.Label(icon.rectTransform, m.HasAttachments && !m.claimed ? "✦" : "✉", 34, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold).rectTransform.Fill();
                var title = Ui.FitLabel(row, Loc.Pick(m.title), 28, UiTheme.Ink, TextAnchor.MiddleLeft, m.read ? FontStyle.Normal : FontStyle.Bold);
                title.rectTransform.Fill(130f, 12f, 20f, 58f);
                var date = Ui.Label(row, Date(m.createdAt) + "  ·  " + Expiry(m.expiresAt), 20, UiTheme.InkSoft, TextAnchor.MiddleLeft);
                date.rectTransform.Fill(130f, 62f, 20f, 12f);
                Ui.AddClick(row.gameObject, () => Select(mail.id));
            }
        }

        static string Date(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime().ToString(Loc.T("format.date"));

        static string Expiry(long ms)
        {
            int days = (int)Math.Ceiling((DateTimeOffset.FromUnixTimeMilliseconds(ms) - DateTimeOffset.UtcNow).TotalDays);
            return Loc.F("mail.expires", Math.Max(0, days));
        }

        async void Select(long id)
        {
            var m = _mail.Find(x => x.id == id);
            if (m == null) return;
            bool changed = _selected != id;
            _selected = id;
            for (int i = _detail.childCount - 1; i >= 0; i--)
                if (_detail.GetChild(i).name != "Shadow") Destroy(_detail.GetChild(i).gameObject);
            var title = Ui.FitLabel(_detail, Loc.Pick(m.title), 40, UiTheme.Ink, TextAnchor.UpperLeft);
            title.rectTransform.At(0f, 1f, 40f, -36f, 1000f, 60f, 0f, 1f);
            var meta = Ui.Label(_detail, Date(m.createdAt) + "  ·  " + Expiry(m.expiresAt), 22, UiTheme.InkSoft, TextAnchor.UpperLeft);
            meta.rectTransform.At(0f, 1f, 40f, -100f, 900f, 34f, 0f, 1f);
            var body = Ui.Label(_detail, Loc.Pick(m.body), 28, UiTheme.Ink, TextAnchor.UpperLeft);
            body.rectTransform.Fill(40f, 150f, 40f, 300f);
            if (m.HasAttachments)
            {
                Ui.Label(_detail, Loc.T(m.claimed ? "mail.claimedGifts" : "mail.gifts"), 26, UiTheme.Primary, TextAnchor.LowerLeft, FontStyle.Bold)
                    .rectTransform.At(0f, 0f, 40f, 230f, 600f, 40f, 0f, 0f);
                var row = Ui.Node("Gifts", _detail).At(0f, 0f, 40f, 150f, 1000f, 70f, 0f, 0f);
                Ui.HStack(row, 14f, 0, TextAnchor.MiddleLeft);
                RewardView.Add(row, m.attachments, 64f);
            }
            if (m.HasAttachments && !m.claimed)
            {
                var claim = Ui.Button(_detail, Loc.T("mail.claim"), () => Claim(m.id, false), ButtonStyle.Accent, 30);
                claim.GetComponent<RectTransform>().At(1f, 0f, -40f, 30f, 340f, 90f, 1f, 0f);
            }
            else
            {
                var delete = Ui.Button(_detail, Loc.T("mail.delete"), () => Delete(m.id), ButtonStyle.Secondary, 28);
                delete.GetComponent<RectTransform>().At(1f, 0f, -40f, 30f, 300f, 84f, 1f, 0f);
            }
            if (!m.read)
            {
                m.read = true;
                try
                {
                    await App.Backend.MarkMailReadAsync(m.id);
                }
                catch (BackendException)
                {
                    // Read state is cosmetic; try again next time.
                }
                if (this == null) return;
                Rebuild();
                await App.RefreshMailCountsAsync();
            }
            else if (changed)
            {
                Rebuild();
            }
        }

        void ClaimAll() => Claim(0, true);

        async void Claim(long id, bool all)
        {
            MailClaimResponse r = null;
            if (!await App.Ui.Busy(Loc.T("common.working"), async () => r = await App.Backend.ClaimMailAsync(id, all))) return;
            App.SetProfile(r.profile);
            if (r.rewards != null && r.rewards.Count > 0) App.Ui.Toast(Loc.F("mail.received", RewardView.Summary(r.rewards)), 4f);
            else if (all) App.Ui.Toast(Loc.T("mail.nothing"));
            if (this == null) return;
            foreach (var claimed in r.claimed)
            {
                var m = _mail.Find(x => x.id == claimed);
                if (m != null) m.claimed = m.read = true;
            }
            Rebuild();
            if (_selected != 0) Select(_selected);
            await App.RefreshMailCountsAsync();
        }

        async void Delete(long id)
        {
            if (!await App.Ui.Busy(Loc.T("common.working"), () => App.Backend.DeleteMailAsync(id))) return;
            _mail.RemoveAll(x => x.id == id);
            _selected = _mail.Count > 0 ? _mail[0].id : 0;
            for (int i = _detail.childCount - 1; i >= 0; i--)
                if (_detail.GetChild(i).name != "Shadow") Destroy(_detail.GetChild(i).gameObject);
            _status.text = _mail.Count == 0 ? Loc.T("mail.empty") : string.Empty;
            Rebuild();
            if (_selected != 0) Select(_selected);
        }
    }
}

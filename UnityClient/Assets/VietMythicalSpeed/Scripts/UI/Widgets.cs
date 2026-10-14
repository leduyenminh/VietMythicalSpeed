using UnityEngine;
using UnityEngine.UI;
using VMS.Core;

namespace VMS.UI
{
    public static class Backdrop
    {
        /// <summary>Bright sky with drifting clouds and thin geometric lines – the default menu background.</summary>
        public static void Sky(RectTransform root)
        {
            var bg = Ui.Node("Backdrop", root).Fill(-200f, -200f, -200f, -200f);
            bg.SetAsFirstSibling();
            Ui.Gradient(bg, UiTheme.SkyTop, UiTheme.SkyBottom).rectTransform.Fill();
            if (UiTheme.Flat) return; // flat UI: the plain sky colour only

            var watermark = Ui.Label(bg, "雲", 760, UiTheme.Primary.WithAlpha(0.05f), TextAnchor.MiddleCenter, FontStyle.Bold, "Watermark");
            watermark.rectTransform.At(1f, 0f, -260f, 360f, 900f, 900f);

            for (int i = 0; i < 3; i++)
            {
                var line = Ui.Skew(bg, Color.white.WithAlpha(0.5f), Color.white.WithAlpha(0.5f), 0f, "Line" + i);
                line.rectTransform.At(0.5f, 0.5f, -300f + i * 420f, 120f - i * 60f, 3200f, 3f);
                line.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 18f);
            }

            var cloudSpecs = new[]
            {
                new Vector4(0.12f, 0.82f, 820f, 240f), new Vector4(0.55f, 0.92f, 960f, 260f), new Vector4(0.9f, 0.7f, 760f, 220f),
                new Vector4(0.3f, 0.18f, 1100f, 300f), new Vector4(0.82f, 0.12f, 900f, 260f),
            };
            for (int i = 0; i < cloudSpecs.Length; i++)
            {
                var s = cloudSpecs[i];
                var cloud = Ui.Image(bg, UiSprites.Glow, Color.white.WithAlpha(0.75f), "Cloud" + i);
                cloud.rectTransform.At(s.x, s.y, 0f, 0f, s.z, s.w);
                var bob = cloud.gameObject.AddComponent<Bob>();
                bob.amplitude = new Vector2(40f, 6f);
                bob.speed = 0.15f + i * 0.03f;
                bob.phase = i * 1.3f;
            }
        }

        /// <summary>Night shrine for the xin xăm ceremony: lacquer red, gold rays, floating lanterns.</summary>
        public static RectTransform Shrine(RectTransform root)
        {
            var bg = Ui.Node("Shrine", root).Fill(-200f, -200f, -200f, -200f);
            bg.SetAsFirstSibling();
            Ui.Gradient(bg, UiTheme.Hex("#2A0710"), UiTheme.Hex("#0D0306")).rectTransform.Fill();
            if (UiTheme.Flat) return bg;
            var rays = Ui.Node("Rays", bg).At(0.5f, 0.5f, 0f, 60f, 10f, 10f);
            for (int i = 0; i < 12; i++)
            {
                var ray = Ui.Skew(rays, UiTheme.Gold.WithAlpha(0.0f), UiTheme.Gold.WithAlpha(0.13f), 120f, "Ray" + i);
                ray.rectTransform.At(0.5f, 0.5f, 0f, 0f, 160f, 1500f, 0.5f, 0f);
                ray.rectTransform.localRotation = Quaternion.Euler(0f, 0f, i * 30f);
            }
            rays.gameObject.AddComponent<Spin>().degreesPerSecond = 6f;
            for (int i = 0; i < 8; i++)
            {
                var lantern = Ui.Image(bg, UiSprites.Glow, UiTheme.Hex("#FF9F45").WithAlpha(0.55f), "Lantern" + i);
                float x = (i * 0.137f + 0.07f) % 1f, y = 0.2f + ((i * 0.31f) % 0.7f);
                lantern.rectTransform.At(x, y, 0f, 0f, 120f, 150f);
                var bob = lantern.gameObject.AddComponent<Bob>();
                bob.amplitude = new Vector2(10f, 22f);
                bob.speed = 0.5f + i * 0.07f;
                bob.phase = i;
            }
            return bg;
        }
    }

    /// <summary>Jade / coins / incense pills, refreshed whenever the profile changes.</summary>
    public sealed class CurrencyBar : MonoBehaviour
    {
        public static readonly Color JadeColor = UiTheme.Hex("#21B39A");
        public static readonly Color CoinColor = UiTheme.Hex("#E8A800");
        public static readonly Color TicketColor = UiTheme.Hex("#F26B3A");
        public static readonly Color GoldColor = UiTheme.Hex("#D99A00");
        public const string GoldGlyph = "▰";

        Text _jade, _coins, _tickets, _gold;

        public static CurrencyBar Create(RectTransform parent)
        {
            var root = Ui.Node("Currencies", parent);
            root.anchorMin = root.anchorMax = new Vector2(1f, 1f);
            root.pivot = new Vector2(1f, 1f);
            root.anchoredPosition = new Vector2(-150f, -26f);
            root.sizeDelta = new Vector2(930f, 70f);
            Ui.HStack(root, 12f, 0, TextAnchor.MiddleRight).childForceExpandHeight = true;
            var bar = root.gameObject.AddComponent<CurrencyBar>();
            RectTransform pill;
            bar._gold = Ui.Pill(root, GoldGlyph, GoldColor, "0", out pill);
            pill.Layout(230f, 70f);
            pill.Find("Bg").GetComponent<Image>().raycastTarget = true;
            // A small "+" badge on the ingot icon: tap to buy more.
            var icon = pill.Find("Icon");
            var plusBg = Ui.Image(icon, UiSprites.Circle, Color.white, "PlusBg");
            plusBg.rectTransform.At(1f, 1f, 6f, 6f, 24f, 24f, 1f, 1f);
            var plus = Ui.Label(plusBg.rectTransform, "+", 22, GoldColor, TextAnchor.MiddleCenter, FontStyle.Bold, "Plus");
            plus.rectTransform.Fill();
            // Tapping the gold balance opens the shop's gold packs.
            Ui.AddClick(pill.gameObject, () =>
            {
                var app = GameApp.Instance;
                if (app != null && !(app.Ui.Current is ShopScreen)) app.Ui.Show<ShopScreen>(ShopScreen.GoldTab);
            });
            bar._jade = Ui.Pill(root, "◆", JadeColor, "0", out pill);
            pill.Layout(230f, 70f);
            bar._coins = Ui.Pill(root, "●", CoinColor, "0", out pill);
            pill.Layout(250f, 70f);
            bar._tickets = Ui.Pill(root, "✦", TicketColor, "0", out pill);
            pill.Layout(170f, 70f);
            bar.Refresh();
            return bar;
        }

        void OnEnable()
        {
            if (GameApp.Instance != null) GameApp.Instance.ProfileChanged += Refresh;
        }

        void OnDisable()
        {
            if (GameApp.Instance != null) GameApp.Instance.ProfileChanged -= Refresh;
        }

        public void Refresh()
        {
            var p = GameApp.Instance != null ? GameApp.Instance.Profile : null;
            if (p == null || _jade == null) return;
            _jade.text = p.jade.ToString("N0");
            _coins.text = p.coins.ToString("N0");
            _tickets.text = p.tickets.ToString("N0");
            _gold.text = p.TotalGold.ToString("N0");
        }
    }

    /// <summary>"Offline · N races waiting" pill. Polls the backend each frame (two field reads), so it needs no wiring.</summary>
    public sealed class ConnectionBadge : MonoBehaviour
    {
        GameObject _body;
        Text _label;
        bool _connected = true;
        int _pending = -1;

        public static ConnectionBadge Create(RectTransform parent, float x, float y)
        {
            var root = Ui.Node("Connection", parent).At(0f, 1f, x, y, 620f, 54f, 0f, 1f);
            var badge = root.gameObject.AddComponent<ConnectionBadge>();
            var body = Ui.Node("Body", root).Fill();
            Ui.Image(body, UiSprites.Rounded, UiTheme.Hex("#34435C").WithAlpha(0.92f), "Bg").rectTransform.Fill();
            Ui.Image(body, UiSprites.Circle, UiTheme.Hex("#FFB547"), "Dot").rectTransform.At(0f, 0.5f, 30f, 0f, 18f, 18f);
            badge._label = Ui.FitLabel(body, string.Empty, 24, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
            badge._label.rectTransform.Fill(54f, 4f, 20f, 4f);
            badge._body = body.gameObject;
            badge._body.SetActive(false);
            return badge;
        }

        void Update()
        {
            var backend = GameApp.Instance != null ? GameApp.Instance.Backend : null;
            if (backend == null) return;
            bool connected = backend.IsConnected;
            int pending = backend.PendingRaceCount;
            if (connected == _connected && pending == _pending) return;
            _connected = connected;
            _pending = pending;
            _body.SetActive(!connected || pending > 0);
            _label.text = !connected
                ? (pending > 0 ? Loc.F("net.offlinePending", pending) : Loc.T("net.offline"))
                : Loc.F("net.pending", pending);
        }
    }

    /// <summary>Blocking dialog shown when the server no longer accepts this game version.</summary>
    public static class UpdatePrompt
    {
        static bool _shown;

        public static void Show(string minVersion, string updateUrl)
        {
            if (_shown || UiRoot.Instance == null) return;
            _shown = true;
            string url = !string.IsNullOrEmpty(updateUrl) ? updateUrl
                : Application.platform == RuntimePlatform.Android ? "market://details?id=" + Application.identifier : string.Empty;

            var p = Popup.Open(Loc.T("update.title"), 1060f, 600f, false);
            string body = Loc.F("update.body", Application.version, string.IsNullOrEmpty(minVersion) ? "?" : minVersion);
            if (string.IsNullOrEmpty(url)) body += "\n" + Loc.T("update.manual");
            Ui.Label(p.Body, body, UiTheme.BodySize, UiTheme.Ink, TextAnchor.UpperLeft).rectTransform.Fill(0f, 0f, 0f, 120f);
            bool canOpen = !string.IsNullOrEmpty(url);
            var quit = Ui.Button(p.Body, Loc.T("update.quit"), Application.Quit, ButtonStyle.Secondary);
            quit.GetComponent<RectTransform>().At(0.5f, 0f, canOpen ? -190f : 0f, 0f, 340f, 90f, 0.5f, 0f);
            if (canOpen)
            {
                var update = Ui.Button(p.Body, Loc.T("update.button"), () => Application.OpenURL(url), ButtonStyle.Accent);
                update.GetComponent<RectTransform>().At(0.5f, 0f, 190f, 0f, 340f, 90f, 0.5f, 0f);
            }
        }
    }

    /// <summary>Rewards as pills (currency glyph + amount, or an item's name).</summary>
    public static class RewardView
    {
        public static string Glyph(string type)
        {
            switch (type)
            {
                case Currencies.Gold: return CurrencyBar.GoldGlyph;
                case Currencies.Jade: return "◆";
                case Currencies.Coins: return "●";
                case Currencies.Tickets: return "✦";
                default: return "★";
            }
        }

        public static Color ColorOf(string type)
        {
            switch (type)
            {
                case Currencies.Gold: return CurrencyBar.GoldColor;
                case Currencies.Jade: return CurrencyBar.JadeColor;
                case Currencies.Coins: return CurrencyBar.CoinColor;
                case Currencies.Tickets: return CurrencyBar.TicketColor;
                default: return UiTheme.Lacquer;
            }
        }

        public static string Text(Reward r)
        {
            if (r.type == Currencies.Item)
            {
                var item = GameApp.Instance != null ? GameApp.Instance.Shop.Item(r.itemId) : null;
                return item != null ? Loc.Pick(item.name) : r.itemId;
            }
            return "+" + r.amount.ToString("N0");
        }

        /// <summary>Adds one pill per reward to a horizontal layout.</summary>
        public static void Add(RectTransform row, System.Collections.Generic.IEnumerable<Reward> rewards, float height = 64f)
        {
            if (rewards == null) return;
            foreach (var r in rewards)
            {
                RectTransform pill;
                var label = Ui.Pill(row, Glyph(r.type), ColorOf(r.type), Text(r), out pill);
                label.fontSize = Mathf.RoundToInt(height * 0.4f);
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 14;
                label.resizeTextMaxSize = Mathf.RoundToInt(height * 0.42f);
                float width = r.type == Currencies.Item ? 300f : 180f;
                pill.Layout(width, height);
            }
        }

        /// <summary>One line summary ("+300 ◆, +1 Sen Hồng").</summary>
        public static string Summary(System.Collections.Generic.IEnumerable<Reward> rewards)
        {
            var parts = new System.Collections.Generic.List<string>();
            if (rewards != null)
                foreach (var r in rewards) parts.Add(r.type == Currencies.Item ? Text(r) : Text(r) + " " + Glyph(r.type));
            return string.Join(", ", parts);
        }
    }

    public static class Format
    {
        public static string Duration(float seconds)
        {
            int s = Mathf.Max(0, Mathf.RoundToInt(seconds));
            return (s / 60) + ":" + (s % 60).ToString("00");
        }

        public static string Percent(float v) => (v * 100f).ToString("0.00") + "%";

        /// <summary>Fills {v} (value, % for fractional passives) and {d} (duration) in skill descriptions.</summary>
        public static string Skill(string template, float value, float duration, string type)
        {
            if (string.IsNullOrEmpty(template)) return string.Empty;
            string v;
            if (type == SkillTypes.WidePerfect || type == PassiveTypes.Score || type == PassiveTypes.Spirit ||
                type == PassiveTypes.Coins || type == PassiveTypes.Bond || type == PassiveTypes.PerfectWindow)
                v = Mathf.RoundToInt(value * 100f).ToString();
            else if (type == SkillTypes.ScoreMult || type == SkillTypes.SpiritBoost)
                v = value.ToString("0.##");
            else
                v = Mathf.Max(1, Mathf.RoundToInt(value)).ToString();
            return template.Replace("{v}", v).Replace("{d}", duration.ToString("0.#"));
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VMS.Backend;
using VMS.Core;

namespace VMS.UI
{
    /// <summary>Lobby: the chosen navigator stands in the centre and talks when tapped (Riichi City / Blue Archive style).</summary>
    public sealed class HomeScreen : UiScreen
    {
        static string _dailyCheckedDay;

        RectTransform _bubble, _figure;
        Text _bubbleText, _bondText;
        RectTransform _figureHolder, _mailBadge, _eventBadge;
        Text _mailCount, _eventCount;
        float _bubbleUntil;

        protected override void Build(object arg)
        {
            Backdrop.Sky(Root);
            var nav = App.SelectedNavigator;

            _figureHolder = Ui.Node("Figure", Root).At(0.34f, 0f, 0f, 0f, 700f, 1000f, 0.5f, 0f);
            BuildFigure(nav);

            // Top-left: avatar in its frame and the player's nameplate (both shop cosmetics). Tap for the profile card.
            var plateItem = CosmeticArt.Equipped(ShopCategories.ProfileFrame);
            var plateHolder = Ui.Node("PlayerPlate", Root).At(0f, 1f, 120f, -30f, 580f, 100f, 0f, 1f);
            CosmeticArt.Plate(plateHolder, plateItem).Fill();
            var account = App.Auth.Account;
            Color plateText = CosmeticArt.PlateText(plateItem);
            var name = Ui.FitLabel(plateHolder, account != null ? account.displayName : "?", 36, plateText, TextAnchor.MiddleLeft);
            name.rectTransform.Fill(96f, 8f, 50f, 40f);
            var provider = Ui.Label(plateHolder, Loc.T("provider." + (App.Auth.Provider ?? "device")), 22, plateText.WithAlpha(0.75f), TextAnchor.LowerLeft, FontStyle.Bold);
            provider.rectTransform.Fill(96f, 58f, 30f, 8f);
            var avatar = CosmeticArt.Avatar(Root, nav, CosmeticArt.Equipped(ShopCategories.AvatarFrame), 150f);
            avatar.At(0f, 1f, 24f, -6f, 150f, 150f, 0f, 1f);
            var hitArea = Ui.Image(Root, UiSprites.White, new Color(0f, 0f, 0f, 0f), "ProfileHit");
            hitArea.rectTransform.At(0f, 1f, 24f, -6f, 680f, 150f, 0f, 1f);
            hitArea.raycastTarget = true;
            Ui.AddClick(hitArea.gameObject, ProfileCard.Show);

            ConnectionBadge.Create(Root, 40f, -170f);
            CurrencyBar.Create(Root);
            var settings = Ui.RoundButton(Root, "⚙", () => App.Ui.Show<SettingsScreen>(), Color.white, UiTheme.PrimaryDark, "Settings");
            settings.GetComponent<RectTransform>().At(1f, 1f, -36f, -24f, 92f, 92f, 1f, 1f);

            // Left column: events and mail, with badges.
            _eventBadge = SideButton("★", Loc.T("home.events"), -250f, () => App.Ui.Show<EventsScreen>(), out _eventCount);
            _mailBadge = SideButton("✉", Loc.T("home.mail"), -390f, () => App.Ui.Show<MailScreen>(), out _mailCount);
            SideButton("◎", Loc.T("home.world"), -530f, () => App.Ui.Show<WorldScreen>(), out _).gameObject.SetActive(false);
            RefreshBadges();

            // Right: big slanted menu.
            var menu = Ui.Node("Menu", Root).At(1f, 0.5f, -90f, -40f, 660f, 700f, 1f, 0.5f);
            Ui.VStack(menu, 22f);
            MenuButton(menu, Loc.T("home.race"), Loc.T("home.raceSub"), ButtonStyle.Accent, 150f, () => App.Ui.Show<SongSelectScreen>());
            MenuButton(menu, Loc.T("home.navigators"), Loc.T("home.navigatorsSub"), ButtonStyle.Primary, 116f, () => App.Ui.Show<NavigatorsScreen>());
            MenuButton(menu, Loc.T("home.gacha"), Loc.T("home.gachaSub"), ButtonStyle.Primary, 116f, () => App.Ui.Show<GachaScreen>());
            var row = Ui.Node("ShopRow", menu);
            row.Layout(-1f, 110f);
            var h = Ui.HStack(row, 18f);
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = true;
            MenuButton(row, Loc.T("home.shop"), Loc.T("home.shopSub"), ButtonStyle.Secondary, 110f, () => App.Ui.Show<ShopScreen>());
            MenuButton(row, Loc.T("home.garage"), Loc.T("home.garageSub"), ButtonStyle.Secondary, 110f, () => App.Ui.Show<GarageScreen>());

            // The navigator's speech bubble floats above the menu instead of hiding behind it.
            _bubble.SetParent(Root, true);
            _bubble.SetAsLastSibling();

            _bondText = Ui.Label(Root, string.Empty, 26, UiTheme.InkSoft, TextAnchor.LowerLeft, FontStyle.Bold);
            _bondText.rectTransform.At(0f, 0f, 40f, 30f, 900f, 40f, 0f, 0f);
            RefreshBond();

            CheckDailyReward();
        }

        RectTransform SideButton(string glyph, string label, float y, System.Action onClick, out Text count)
        {
            var b = Ui.RoundButton(Root, glyph, onClick, Color.white, UiTheme.PrimaryDark, label);
            var rt = b.GetComponent<RectTransform>();
            rt.At(0f, 1f, 50f, y, 96f, 96f, 0f, 1f);
            var caption = Ui.Label(rt, label, 22, UiTheme.PrimaryDark, TextAnchor.UpperCenter, FontStyle.Bold);
            caption.rectTransform.At(0.5f, 0f, 0f, -4f, 160f, 30f, 0.5f, 1f);
            caption.gameObject.AddComponent<Outline>().effectColor = Color.white.WithAlpha(0.8f);
            var badge = Ui.Image(rt, UiSprites.Circle, UiTheme.Lacquer, "Badge");
            badge.rectTransform.At(1f, 1f, 6f, 6f, 40f, 40f, 1f, 1f);
            count = Ui.Label(badge.rectTransform, string.Empty, 22, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            count.rectTransform.Fill();
            return badge.rectTransform;
        }

        void OnEnable()
        {
            if (App != null) App.LiveDataChanged += RefreshBadges;
        }

        void OnDisable()
        {
            if (App != null) App.LiveDataChanged -= RefreshBadges;
        }

        void RefreshBadges()
        {
            if (_mailBadge == null) return;
            int mail = App.MailUnclaimed > 0 ? App.MailUnclaimed : App.MailUnread;
            _mailBadge.gameObject.SetActive(mail > 0);
            _mailCount.text = mail > 9 ? "9+" : mail.ToString();
            int events = App.ClaimableEventRewards;
            _eventBadge.gameObject.SetActive(events > 0);
            _eventCount.text = events > 9 ? "9+" : events.ToString();
        }

        void BuildFigure(NavigatorDef nav)
        {
            for (int i = _figureHolder.childCount - 1; i >= 0; i--) Destroy(_figureHolder.GetChild(i).gameObject);
            _figure = NavigatorArt.Standing(_figureHolder, nav, 960f);
            _figure.anchorMin = _figure.anchorMax = new Vector2(0.5f, 0f);
            _figure.pivot = new Vector2(0.5f, 0f);
            _figure.anchoredPosition = Vector2.zero;
            var hit = Ui.Image(_figureHolder, UiSprites.White, new Color(0f, 0f, 0f, 0f), "Tap");
            hit.rectTransform.Fill();
            hit.raycastTarget = true;
            Ui.AddClick(hit.gameObject, () => Talk(VoiceTriggers.Greet));

            var nameTag = Ui.Skew(_figureHolder, UiTheme.NavigatorColor(nav), Color.Lerp(UiTheme.NavigatorColor(nav), UiTheme.Ink, 0.4f), 30f, "NameTag");
            nameTag.rectTransform.At(0.5f, 0f, -40f, 60f, 520f, 80f);
            var tag = Ui.FitLabel(nameTag.rectTransform, Loc.Pick(nav.name) + "  " + UiTheme.RarityStars(nav.rarity), 34, Color.white, TextAnchor.MiddleCenter);
            tag.rectTransform.Fill(30f, 4f, 30f, 4f);

            // Emotions (shop cosmetics): a little face button next to the navigator.
            var emote = Ui.RoundButton(_figureHolder, "☺", ShowEmotions, Color.white, UiTheme.Lacquer, "Emote");
            emote.GetComponent<RectTransform>().At(0.5f, 0f, 260f, 180f, 92f, 92f);

            _bubble = Ui.Node("Bubble", _figureHolder).At(0.5f, 1f, 360f, -60f, 560f, 170f, 0f, 1f);
            var bg = Ui.Image(_bubble, UiSprites.Rounded, Color.white.WithAlpha(0.97f), "Bg");
            bg.rectTransform.Fill();
            _bubbleText = Ui.Label(_bubble, string.Empty, 30, UiTheme.Ink, TextAnchor.MiddleLeft);
            _bubbleText.rectTransform.Fill(30f, 16f, 30f, 16f);
            CosmeticArt.ApplyBubble(_bubble, bg, null, _bubbleText, CosmeticArt.Equipped(ShopCategories.BubbleFrame));
            _bubble.gameObject.SetActive(false);
        }

        static void MenuButton(RectTransform parent, string title, string subtitle, ButtonStyle style, float height, System.Action onClick)
        {
            var b = Ui.Button(parent, title, onClick, style, height > 130f ? 64 : 46);
            b.Layout(-1f, height, 1f);
            var label = b.GetComponentInChildren<Text>();
            label.alignment = TextAnchor.UpperLeft;
            label.fontStyle = FontStyle.BoldAndItalic;
            label.rectTransform.Fill(50f, 14f, 30f, height * 0.38f);
            var sub = Ui.Label(b.transform, subtitle, 22, style == ButtonStyle.Accent || style == ButtonStyle.Secondary ? UiTheme.InkSoft : Color.white.WithAlpha(0.85f), TextAnchor.LowerLeft);
            sub.rectTransform.Fill(54f, height * 0.55f, 30f, 14f);
        }

        void Say(string text, string face)
        {
            _bubbleText.text = text;
            _bubble.gameObject.SetActive(true);
            _bubble.localScale = Vector3.one * 0.9f;
            _bubbleUntil = Time.unscaledTime + 4.5f;
            NavigatorArt.SetEmotion(_figure, face);
        }

        void Talk(string trigger)
        {
            var nav = App.SelectedNavigator;
            int bond = App.BondLevel(nav.id);
            var options = new List<VoiceLine>(nav.LinesFor(trigger, bond));
            options.AddRange(nav.LinesFor(VoiceTriggers.Idle, bond));
            if (options.Count == 0) return;
            var line = options[Random.Range(0, options.Count)];
            Say(Loc.Pick(line.text), "smile");
            var clip = NavigatorArt.Voice(nav.id, line.id);
            if (clip != null) App.Audio.Voice(clip);
        }

        /// <summary>The navigator performs an owned emotion (expression + line).</summary>
        public void Emote(ShopItem emotion)
        {
            if (emotion == null || emotion.style == null) return;
            Say(Loc.Pick(emotion.style.text), emotion.style.face);
        }

        /// <summary>Emotions the player owns; locked ones link to the shop.</summary>
        public void ShowEmotions()
        {
            var p = Popup.Open(Loc.T("home.emotions"), 1200f, 640f);
            var grid = Ui.Node("Grid", p.Body).Fill(0f, 0f, 0f, 0f);
            var layout = grid.gameObject.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(250f, 230f);
            layout.spacing = new Vector2(16f, 16f);
            layout.childAlignment = TextAnchor.UpperCenter;
            foreach (var item in App.Shop.InCategory(ShopCategories.Emotion))
            {
                var emotion = item;
                bool owned = ShopRules.Owns(App.Profile, item);
                if (!owned && (item.hidden || !ShopRules.IsOnSale(item, System.DateTime.UtcNow))) continue;
                var cell = Ui.Card(grid, owned ? Color.white : UiTheme.PanelAlt, emotion.id).rectTransform;
                var face = CosmeticArt.FaceIcon(cell, item.style != null ? item.style.face : null, 130f);
                face.At(0.5f, 1f, 0f, -16f, 130f, 130f, 0.5f, 1f);
                Ui.FitLabel(cell, Loc.Pick(item.name), 26, UiTheme.Ink, TextAnchor.MiddleCenter).rectTransform.At(0.5f, 0f, 0f, 44f, 230f, 36f, 0.5f, 0f);
                if (!owned)
                {
                    foreach (var g in face.GetComponentsInChildren<Graphic>()) g.color = g.color.WithAlpha(g.color.a * 0.45f);
                    Ui.Label(cell, ShopScreen.PriceText(item.price), 22, UiTheme.PrimaryDark, TextAnchor.MiddleCenter, FontStyle.Bold).rectTransform.At(0.5f, 0f, 0f, 10f, 230f, 30f, 0.5f, 0f);
                }
                Ui.AddClick(cell.gameObject, () =>
                {
                    p.Close();
                    if (owned) Emote(emotion);
                    else App.Ui.Show<ShopScreen>(ShopScreen.ItemArg(emotion.id));
                });
            }
        }

        void Update()
        {
            if (_bubble == null || !_bubble.gameObject.activeSelf) return;
            _bubble.localScale = Vector3.Lerp(_bubble.localScale, Vector3.one, Time.unscaledDeltaTime * 14f);
            if (Time.unscaledTime > _bubbleUntil)
            {
                _bubble.gameObject.SetActive(false);
                NavigatorArt.SetEmotion(_figure, null);
            }
        }

        void RefreshBond()
        {
            var nav = App.SelectedNavigator;
            var owned = App.Profile != null ? App.Profile.FindNavigator(nav.id) : null;
            if (owned == null)
            {
                _bondText.text = string.Empty;
                return;
            }
            var bond = App.Data.Economy.bond;
            int level = EconomyRules.BondLevel(bond, owned.bondExp);
            _bondText.text = Loc.F("home.bond", Loc.Pick(nav.name), level, EconomyRules.MaxBondLevel(bond));
        }

        public override void OnProfileChanged()
        {
            RefreshBond();
            RefreshBadges();
        }

        public override bool OnBack()
        {
            Popup.Confirm(Loc.T("home.quitTitle"), Loc.T("home.quitBody"), Loc.T("home.quit"), Application.Quit, null, ButtonStyle.Danger);
            return true;
        }

        async void CheckDailyReward()
        {
            if (App.Profile == null) return;
            // Per account, so switching accounts on the same day still gives the new one its login reward.
            string today = App.Profile.accountId + "|" + EconomyRules.DayKey(System.DateTime.UtcNow);
            if (_dailyCheckedDay == today) return;
            _dailyCheckedDay = today;
            Talk(VoiceTriggers.Greet);
            try
            {
                var response = await App.Backend.ClaimDailyAsync();
                App.SetProfile(response.profile);
                if (response.result != null && (response.result.claimed || response.result.passJade > 0)) ShowDaily(response.result);
            }
            catch (BackendException e)
            {
                // Offline: try again the next time the lobby opens.
                if (e.IsConnectivity) _dailyCheckedDay = null;
                Debug.LogWarning("[Home] Daily reward: " + e.Code + " " + e.Message);
            }
        }

        static void ShowDaily(DailyClaimResult r)
        {
            var p = Popup.Open(Loc.T("daily.title"), 980f, 600f);
            var text = Ui.Label(p.Body, Loc.F("daily.body", r.streak), UiTheme.BodySize, UiTheme.Ink, TextAnchor.UpperCenter);
            text.rectTransform.Fill(0f, 0f, 0f, 300f);
            var rewards = Ui.Node("Rewards", p.Body).At(0.5f, 0.5f, 0f, 10f, 860f, 100f);
            Ui.HStack(rewards, 24f, 0, TextAnchor.MiddleCenter);
            var list = new List<Reward>();
            if (r.claimed) list.Add(new Reward { type = Currencies.Jade, amount = r.jade });
            if (r.tickets > 0) list.Add(new Reward { type = Currencies.Tickets, amount = r.tickets });
            if (r.passJade > 0) list.Add(new Reward { type = Currencies.Jade, amount = r.passJade });
            RewardView.Add(rewards, list, 80f);
            if (r.passJade > 0)
            {
                var pass = Ui.Label(p.Body, Loc.F("daily.pass", r.passJade), 24, UiTheme.InkSoft, TextAnchor.MiddleCenter);
                pass.rectTransform.At(0.5f, 0f, 0f, 110f, 860f, 40f, 0.5f, 0f);
            }
            var ok = Ui.Button(p.Body, Loc.T("common.ok"), p.Close, ButtonStyle.Accent);
            ok.GetComponent<RectTransform>().At(0.5f, 0f, 0f, 0f, 320f, 90f, 0.5f, 0f);
        }
    }

    /// <summary>The player's card: avatar frame, nameplate, collection stats and links to change the look.</summary>
    public static class ProfileCard
    {
        public static void Show()
        {
            var app = GameApp.Instance;
            var profile = app.Profile;
            if (profile == null) return;
            var p = Popup.Open(Loc.T("profile.title"), 1240f, 720f);
            var nav = app.SelectedNavigator;
            var avatar = CosmeticArt.Avatar(p.Body, nav, CosmeticArt.Equipped(ShopCategories.AvatarFrame), 300f);
            avatar.At(0f, 1f, 20f, -10f, 300f, 300f, 0f, 1f);
            var plateItem = CosmeticArt.Equipped(ShopCategories.ProfileFrame);
            var plate = Ui.Node("Plate", p.Body).At(0f, 1f, 360f, -40f, 760f, 130f, 0f, 1f);
            CosmeticArt.Plate(plate, plateItem, 40f).Fill();
            var account = app.Auth.Account;
            Ui.FitLabel(plate, account != null ? account.displayName : "?", 48, CosmeticArt.PlateText(plateItem), TextAnchor.MiddleLeft).rectTransform.Fill(60f, 10f, 40f, 10f);

            int owned = 0, cosmetics = 0;
            foreach (var n in app.Data.AllNavigators()) if (profile.Owns(n.id)) owned++;
            foreach (var id in profile.cosmetics.owned) if (app.Shop.Item(id) != null) cosmetics++;
            var stats = Ui.Label(p.Body, Loc.F("profile.stats", owned, app.Data.Navigators.navigators.Length, cosmetics, profile.TotalGold.ToString("N0"), profile.gold.ToString("N0"), profile.goldBonus.ToString("N0")),
                28, UiTheme.Ink, TextAnchor.UpperLeft);
            stats.rectTransform.At(0f, 1f, 360f, -200f, 780f, 220f, 0f, 1f);

            var buttons = Ui.Node("Buttons", p.Body).At(0.5f, 0f, 0f, 0f, 1100f, 90f, 0.5f, 0f);
            var row = Ui.HStack(buttons, 18f);
            row.childForceExpandWidth = true;
            row.childForceExpandHeight = true;
            Ui.Button(buttons, Loc.T("profile.frames"), () => { p.Close(); app.Ui.Show<ShopScreen>(ShopCategories.AvatarFrame); }, ButtonStyle.Secondary, 26);
            Ui.Button(buttons, Loc.T("profile.plates"), () => { p.Close(); app.Ui.Show<ShopScreen>(ShopCategories.ProfileFrame); }, ButtonStyle.Secondary, 26);
            Ui.Button(buttons, Loc.T("home.garage"), () => { p.Close(); app.Ui.Show<GarageScreen>(); }, ButtonStyle.Primary, 26);
        }
    }
}

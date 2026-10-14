using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VMS.Core;
using VMS.Core.Music;
using VMS.Race;

namespace VMS.UI
{
    public sealed class ResultsScreen : UiScreen
    {
        RaceResult _result;

        protected override void Build(object arg)
        {
            _result = (RaceResult)arg;
            var r = _result;
            Backdrop.Sky(Root);
            Ui.Header(Root, Loc.T("results.title"), () => App.Ui.Back());
            CurrencyBar.Create(Root);
            var nav = App.Data.Navigator(r.setup.navigatorId) ?? App.SelectedNavigator;

            // Navigator with a finishing line.
            var figure = NavigatorArt.Standing(Root, nav, 820f);
            figure.At(0.16f, 0f, 0f, 0f, 520f, 820f, 0.5f, 0f);
            string trigger = r.rank == "S+" || r.rank == "S" || r.rank == "A" ? VoiceTriggers.FinishHigh
                : r.rank == "B" ? VoiceTriggers.FinishMid : VoiceTriggers.FinishLow;
            var line = PickLine(nav, trigger) ?? PickLine(nav, VoiceTriggers.FinishHigh);
            if (line != null)
            {
                var bubble = Ui.Node("Bubble", Root).At(0.16f, 1f, 0f, -140f, 500f, 140f, 0.5f, 1f);
                var bubbleBg = Ui.Image(bubble, UiSprites.Rounded, Color.white.WithAlpha(0.96f), "Bg");
                bubbleBg.rectTransform.Fill();
                var bubbleLine = Ui.Label(bubble, Loc.Pick(line.text), 28, UiTheme.Ink, TextAnchor.MiddleCenter);
                bubbleLine.rectTransform.Fill(24f, 10f, 24f, 10f);
                CosmeticArt.ApplyBubble(bubble, bubbleBg, null, bubbleLine, CosmeticArt.Equipped(ShopCategories.BubbleFrame));
                var clip = NavigatorArt.Voice(nav.id, line.id);
                if (clip != null) App.Audio.Voice(clip);
            }

            var card = Ui.Card(Root, UiTheme.Panel.WithAlpha(0.97f), "Card").rectTransform;
            card.Region(0.33f, 0f, 1f, 1f);
            card.offsetMin = new Vector2(0f, 180f);
            card.offsetMax = new Vector2(-40f, -140f);

            // Rank medallion.
            var medal = Ui.Node("Rank", card).At(0f, 1f, 60f, -40f, 300f, 300f, 0f, 1f);
            var glow = Ui.Image(medal, UiSprites.Glow, RankColor(r.rank).WithAlpha(0.7f), "Glow");
            glow.rectTransform.Fill(-60f, -60f, -60f, -60f);
            glow.gameObject.AddComponent<Spin>().degreesPerSecond = 15f;
            Ui.Image(medal, UiSprites.Circle, RankColor(r.rank), "Disc").rectTransform.Fill();
            Ui.Image(medal, UiSprites.Ring, Color.white.WithAlpha(0.8f), "Ring").rectTransform.Fill(14f, 14f, 14f, 14f);
            var rank = Ui.Label(medal, r.rank, 150, Color.white, TextAnchor.MiddleCenter, FontStyle.BoldAndItalic);
            rank.rectTransform.Fill();
            rank.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(4f, -4f);

            var song = Ui.FitLabel(card, r.setup.song.title, 40, UiTheme.Ink, TextAnchor.UpperLeft);
            song.rectTransform.At(0f, 1f, 400f, -50f, 900f, 60f, 0f, 1f);
            string modeLine = Loc.T("mode." + r.setup.mode.ToString().ToLowerInvariant()) + " · " + Loc.T("difficulty." + r.setup.difficulty.ToString().ToLowerInvariant());
            Ui.Label(card, modeLine, 26, UiTheme.InkSoft).rectTransform.At(0f, 1f, 400f, -110f, 900f, 40f, 0f, 1f);
            var score = Ui.Label(card, r.score.ToString("N0"), 92, UiTheme.PrimaryDark, TextAnchor.UpperLeft, FontStyle.BoldAndItalic);
            score.rectTransform.At(0f, 1f, 396f, -160f, 900f, 110f, 0f, 1f);
            if (r.newBest)
            {
                var badge = Ui.Skew(card, UiTheme.Lacquer, UiTheme.Lacquer, 16f, "NewBest");
                badge.rectTransform.At(0f, 1f, 400f, -280f, 280f, 50f, 0f, 1f);
                Ui.Label(badge.rectTransform, Loc.T("results.newBest"), 26, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold).rectTransform.Fill();
            }

            var stats = Ui.Node("Stats", card).Fill(60f, 380f, 60f, 180f);
            var grid = stats.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(330f, 92f);
            grid.spacing = new Vector2(20f, 16f);
            if (r.setup.mode == GameMode.Dodge)
            {
                Stat(stats, Loc.T("results.dodges"), r.dodges.ToString());
                Stat(stats, Loc.T("results.hits"), r.hazardHits.ToString());
            }
            Stat(stats, Loc.T("judge.perfect"), r.perfect.ToString());
            Stat(stats, Loc.T("judge.good"), r.good.ToString());
            Stat(stats, Loc.T("judge.miss"), r.miss.ToString());
            Stat(stats, Loc.T("results.maxCombo"), r.maxCombo.ToString());
            Stat(stats, Loc.T("results.accuracy"), Format.Percent(r.accuracy));

            // Rewards.
            var rewards = Ui.Node("Rewards", card).At(0f, 0f, 60f, 50f, 1100f, 100f, 0f, 0f);
            Ui.HStack(rewards, 20f, 0, TextAnchor.MiddleLeft);
            Ui.Label(rewards, Loc.T("results.rewards"), 28, UiTheme.Primary, TextAnchor.MiddleLeft, FontStyle.Bold).Layout(200f, 80f);
            if (r.reward != null)
            {
                RectTransform pill;
                Ui.Pill(rewards, "●", CurrencyBar.CoinColor, "+" + r.reward.coins, out pill);
                pill.Layout(230f, 76f);
                if (r.reward.jade > 0)
                {
                    Ui.Pill(rewards, "◆", CurrencyBar.JadeColor, "+" + r.reward.jade, out pill);
                    pill.Layout(200f, 76f);
                }
                if (r.reward.bondExp > 0)
                {
                    Ui.Pill(rewards, "♥", UiTheme.Lacquer, "+" + r.reward.bondExp, out pill);
                    pill.Layout(200f, 76f);
                }
                if (r.reward.bondLevelAfter > r.reward.bondLevelBefore)
                {
                    App.Ui.Toast(Loc.F("results.bondUp", Loc.Pick(nav.name), r.reward.bondLevelAfter), 3.5f);
                    var bondLine = PickLine(nav, VoiceTriggers.BondUp);
                    if (bondLine != null) App.Ui.Toast(Loc.Pick(bondLine.text), 4f);
                }
            }
            else
            {
                string note = r.setup.mode == GameMode.Zen ? "results.zen" : r.rewardPending ? "results.pending" : "results.noReward";
                Ui.Label(rewards, Loc.T(note), 24, UiTheme.InkSoft).Layout(820f, 80f);
            }

            var buttons = Ui.Node("Buttons", Root).At(1f, 0f, -40f, 40f, 1260f, 110f, 1f, 0f);
            Ui.HStack(buttons, 24f, 0, TextAnchor.MiddleRight);
            Ui.Button(buttons, Loc.T("results.home"), () => App.Ui.Reset<HomeScreen>(), ButtonStyle.Secondary, 32).Layout(360f, 110f);
            Ui.Button(buttons, Loc.T("results.songs"), () => App.Ui.Back(), ButtonStyle.Primary, 32).Layout(380f, 110f);
            Ui.Button(buttons, Loc.T("results.retry"), () =>
            {
                // Return to the song list first so repeated retries don't stack result screens in the history.
                App.Ui.Back();
                App.StartRace(r.setup);
            }, ButtonStyle.Accent, 34).Layout(380f, 110f);
        }

        static VoiceLine PickLine(NavigatorDef nav, string trigger)
        {
            var lines = new List<VoiceLine>(nav.LinesFor(trigger, GameApp.Instance.BondLevel(nav.id)));
            return lines.Count > 0 ? lines[Random.Range(0, lines.Count)] : null;
        }

        static void Stat(RectTransform parent, string label, string value)
        {
            var cell = Ui.Node("Stat " + label, parent);
            Ui.Image(cell, UiSprites.Rounded, UiTheme.PanelAlt, "Bg").rectTransform.Fill();
            Ui.Label(cell, label, 24, UiTheme.InkSoft, TextAnchor.MiddleLeft).rectTransform.Fill(24f, 0f, 120f, 0f);
            Ui.Label(cell, value, 34, UiTheme.Ink, TextAnchor.MiddleRight, FontStyle.Bold).rectTransform.Fill(100f, 0f, 24f, 0f);
        }

        static Color RankColor(string rank)
        {
            switch (rank)
            {
                case "S+": return UiTheme.Hex("#FF4FA3");
                case "S": return UiTheme.Hex("#FFB800");
                case "A": return UiTheme.Primary;
                case "B": return UiTheme.Hex("#2BB673");
                case "C": return UiTheme.Hex("#8E7CC3");
                case "-": return UiTheme.InkFaint;
                default: return UiTheme.InkSoft;
            }
        }
    }
}

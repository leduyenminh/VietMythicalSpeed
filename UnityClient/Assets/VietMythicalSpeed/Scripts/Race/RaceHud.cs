using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using VMS.Core;
using VMS.UI;

namespace VMS.Race
{
    /// <summary>
    /// In-race overlay. Frequently changing elements live on a nested canvas so their updates don't rebuild
    /// the static parts, and text is only re-assigned when its value actually changes.
    /// </summary>
    public sealed class RaceHud : MonoBehaviour
    {
        public Action OnPause, OnResume, OnRestart, OnQuit, OnSkill;

        Text _score, _combo, _countdown, _bubbleText, _skillLabel, _modeLabel;
        Image _progress, _spiritFill, _skillGlow, _activeRing;
        RectTransform _bubble, _skillRoot, _pausePanel, _cutIn;
        Image _bubbleBg;
        Graphic _bubbleTail;
        Text[] _judgements;
        float[] _judgeAge;
        RectTransform[] _judgeRt;
        int _nextJudge;
        long _shownScore = -1;
        int _shownCombo = -1;
        float _bubbleUntil;
        bool _skillsEnabled;
        NavigatorDef _navigator;

        public static RaceHud Create(RectTransform layer, NavigatorDef navigator, bool skillsEnabled, string songTitle, string modeText)
        {
            var root = Ui.Node("RaceHud", layer).Fill();
            var hud = root.gameObject.AddComponent<RaceHud>();
            hud._navigator = navigator;
            hud._skillsEnabled = skillsEnabled;
            hud.Build(root, songTitle, modeText);
            return hud;
        }

        void Build(RectTransform root, string songTitle, string modeText)
        {
            // Dynamic sub-canvas: score, combo, progress, judgements, spirit.
            var dynamicRoot = Ui.Node("Dynamic", root).Fill();
            dynamicRoot.gameObject.AddComponent<Canvas>();
            dynamicRoot.gameObject.AddComponent<GraphicRaycaster>();

            _score = Ui.Label(dynamicRoot, "0", 66, Color.white, TextAnchor.UpperLeft, FontStyle.Bold, "Score");
            _score.rectTransform.At(0f, 1f, 44f, -26f, 700f, 90f, 0f, 1f);
            AddShadow(_score);
            _combo = Ui.Label(dynamicRoot, string.Empty, 34, UiTheme.Accent, TextAnchor.UpperLeft, FontStyle.Bold, "Combo");
            _combo.rectTransform.At(0f, 1f, 48f, -112f, 700f, 50f, 0f, 1f);
            AddShadow(_combo);

            var title = Ui.FitLabel(root, songTitle, 26, Color.white.WithAlpha(0.85f), TextAnchor.LowerCenter, FontStyle.Normal);
            title.rectTransform.At(0.5f, 1f, 0f, -22f, 760f, 36f, 0.5f, 1f);
            var track = Ui.Image(root, UiSprites.RoundedSmall, new Color(1f, 1f, 1f, 0.22f), "ProgressTrack");
            track.rectTransform.At(0.5f, 1f, 0f, -64f, 760f, 10f, 0.5f, 1f);
            _progress = Ui.Image(dynamicRoot, UiSprites.White, UiTheme.Accent, "Progress");
            _progress.type = Image.Type.Filled;
            _progress.fillMethod = Image.FillMethod.Horizontal;
            _progress.rectTransform.At(0.5f, 1f, 0f, -64f, 756f, 6f, 0.5f, 1f);
            _modeLabel = Ui.Label(root, modeText, 24, Color.white.WithAlpha(0.7f), TextAnchor.UpperCenter, FontStyle.Normal, "Mode");
            _modeLabel.rectTransform.At(0.5f, 1f, 0f, -80f, 760f, 34f, 0.5f, 1f);

            var pause = Ui.RoundButton(root, "II", () => OnPause?.Invoke(), new Color(1f, 1f, 1f, 0.9f), UiTheme.PrimaryDark, "Pause");
            pause.GetComponent<RectTransform>().At(1f, 1f, -40f, -28f, 96f, 96f, 1f, 1f);

            _judgements = new Text[3];
            _judgeAge = new float[3];
            _judgeRt = new RectTransform[3];
            for (int i = 0; i < _judgements.Length; i++)
            {
                var t = Ui.Label(dynamicRoot, string.Empty, 52, Color.white, TextAnchor.MiddleCenter, FontStyle.BoldAndItalic, "Judge" + i);
                t.rectTransform.At(0.5f, 0.5f, 0f, 150f, 700f, 90f);
                AddShadow(t);
                t.enabled = false;
                _judgements[i] = t;
                _judgeRt[i] = t.rectTransform;
                _judgeAge[i] = 99f;
            }

            _countdown = Ui.Label(root, string.Empty, 190, Color.white, TextAnchor.MiddleCenter, FontStyle.BoldAndItalic, "Countdown");
            _countdown.rectTransform.At(0.5f, 0.5f, 0f, 80f, 900f, 280f);
            AddShadow(_countdown);

            BuildNavigator(root);
            if (_skillsEnabled) BuildSkill(dynamicRoot);
            BuildPause(root);
            BuildCutIn(root);
        }

        static void AddShadow(Text t)
        {
            var s = t.gameObject.AddComponent<Shadow>();
            s.effectColor = new Color(0.03f, 0.07f, 0.2f, 0.65f);
            s.effectDistance = new Vector2(3f, -3f);
        }

        void BuildNavigator(RectTransform root)
        {
            if (_navigator == null) return;
            var holder = Ui.Node("Navigator", root).At(0f, 0f, 36f, 36f, 168f, 168f, 0f, 0f);
            var ring = Ui.Image(holder, UiSprites.Circle, Color.white, "Ring");
            ring.rectTransform.Fill(-6f, -6f, -6f, -6f);
            var portrait = NavigatorArt.Portrait(holder, _navigator, 168f);
            portrait.anchorMin = portrait.anchorMax = new Vector2(0.5f, 0.5f);
            portrait.anchoredPosition = Vector2.zero;
            var halo = Ui.Image(holder, UiSprites.Halo, UiTheme.Accent, "Halo");
            halo.rectTransform.At(0.5f, 1f, 0f, 46f, 120f, 120f, 0.5f, 1f);

            _bubble = Ui.Node("Bubble", root).At(0f, 0f, 222f, 112f, 660f, 120f, 0f, 0.5f);
            var bg = Ui.Image(_bubble, UiSprites.Rounded, new Color(1f, 1f, 1f, 0.95f), "Bg");
            bg.rectTransform.Fill();
            _bubbleBg = bg;
            var tail = Ui.Skew(_bubble, Color.white, Color.white, -14f, "Tail");
            _bubbleTail = tail;
            tail.rectTransform.At(0f, 0.5f, 6f, -18f, 30f, 26f, 1f, 0.5f);
            var name = Ui.Label(_bubble, Loc.Pick(_navigator.name), 22, UiTheme.NavigatorColor(_navigator), TextAnchor.UpperLeft, FontStyle.Bold, "Name");
            name.rectTransform.Fill(26f, 8f, 20f, 70f);
            _bubbleText = Ui.Label(_bubble, string.Empty, 28, UiTheme.Ink, TextAnchor.UpperLeft, FontStyle.Normal, "Line");
            _bubbleText.rectTransform.Fill(26f, 40f, 20f, 8f);
            _bubble.gameObject.SetActive(false);
        }

        /// <summary>Applies the player's speech-bubble frame (shop cosmetic) to the navigator's lines.</summary>
        public void StyleBubble(ShopItem frame)
        {
            if (_bubble == null || frame == null) return;
            CosmeticArt.ApplyBubble(_bubble, _bubbleBg, _bubbleTail, _bubbleText, frame);
        }

        void BuildSkill(RectTransform dynamicRoot)
        {
            _skillRoot = Ui.Node("Skill", dynamicRoot).At(1f, 0f, -44f, 44f, 196f, 196f, 1f, 0f);
            _skillGlow = Ui.Image(_skillRoot, UiSprites.Glow, UiTheme.Accent, "Glow");
            _skillGlow.rectTransform.Fill(-60f, -60f, -60f, -60f);
            _skillGlow.enabled = false;
            var bg = Ui.Image(_skillRoot, UiSprites.Circle, new Color(0.08f, 0.13f, 0.28f, 0.75f), "Bg");
            bg.rectTransform.Fill();
            bg.raycastTarget = true;
            _spiritFill = Ui.Image(_skillRoot, UiSprites.Ring, UiTheme.Primary, "Spirit");
            _spiritFill.type = Image.Type.Filled;
            _spiritFill.fillMethod = Image.FillMethod.Radial360;
            _spiritFill.fillOrigin = (int)Image.Origin360.Top;
            _spiritFill.rectTransform.Fill();
            _activeRing = Ui.Image(_skillRoot, UiSprites.Ring, UiTheme.Accent, "Active");
            _activeRing.type = Image.Type.Filled;
            _activeRing.fillMethod = Image.FillMethod.Radial360;
            _activeRing.fillOrigin = (int)Image.Origin360.Top;
            _activeRing.rectTransform.Fill(14f, 14f, 14f, 14f);
            _activeRing.enabled = false;
            string skillName = _navigator != null && _navigator.skill != null ? Loc.Pick(_navigator.skill.name) : "Skill";
            _skillLabel = Ui.FitLabel(_skillRoot, skillName, 26, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            _skillLabel.rectTransform.Fill(26f, 40f, 26f, 40f);
            Ui.AddClick(bg.gameObject, () => OnSkill?.Invoke());
            bg.gameObject.AddComponent<PressFeedback>();
        }

        void BuildPause(RectTransform root)
        {
            _pausePanel = Ui.Node("PausePanel", root).Fill();
            var scrim = Ui.Image(_pausePanel, UiSprites.White, UiTheme.Scrim, "Scrim");
            scrim.rectTransform.Fill(-200f, -200f, -200f, -200f);
            scrim.raycastTarget = true;
            var card = Ui.Card(_pausePanel, UiTheme.Panel);
            card.rectTransform.At(0.5f, 0.5f, 0f, 0f, 640f, 560f);
            var title = Ui.Label(card.rectTransform, Loc.T("race.paused"), UiTheme.HeadingSize, UiTheme.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            title.rectTransform.At(0.5f, 1f, 0f, -40f, 560f, 70f, 0.5f, 1f);
            var stack = Ui.Node("Buttons", card.rectTransform).Fill(80f, 140f, 80f, 50f);
            Ui.VStack(stack, 26f);
            Ui.Button(stack, Loc.T("race.resume"), () => OnResume?.Invoke(), ButtonStyle.Accent).Layout(-1f, 96f);
            Ui.Button(stack, Loc.T("race.restart"), () => OnRestart?.Invoke(), ButtonStyle.Secondary).Layout(-1f, 96f);
            Ui.Button(stack, Loc.T("race.quit"), () => OnQuit?.Invoke(), ButtonStyle.Danger).Layout(-1f, 96f);
            _pausePanel.gameObject.SetActive(false);
        }

        void BuildCutIn(RectTransform root)
        {
            _cutIn = Ui.Node("SkillCutIn", root).At(0.5f, 0.5f, 0f, 260f, 2600f, 210f);
            var band = Ui.Skew(_cutIn, UiTheme.Hex("#4FB0FF"), UiTheme.PrimaryDark, 120f, "Band");
            band.rectTransform.Fill();
            var stripe = Ui.Skew(_cutIn, UiTheme.Accent, UiTheme.Accent, 120f, "Stripe");
            stripe.rectTransform.At(0.5f, 0f, 0f, 0f, 2600f, 12f, 0.5f, 0f);
            if (_navigator != null)
            {
                var portrait = NavigatorArt.Portrait(_cutIn, _navigator, 190f);
                portrait.anchorMin = portrait.anchorMax = new Vector2(0.5f, 0.5f);
                portrait.anchoredPosition = new Vector2(-420f, 0f);
                var label = Ui.Label(_cutIn, _navigator.skill != null ? Loc.Pick(_navigator.skill.name) : string.Empty, 64, Color.white, TextAnchor.MiddleLeft, FontStyle.BoldAndItalic, "SkillName");
                label.rectTransform.At(0.5f, 0.5f, 230f, 0f, 900f, 120f);
                AddShadow(label);
            }
            _cutIn.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ updates

        public void SetScore(long score, int combo, float multiplier)
        {
            if (score != _shownScore)
            {
                _shownScore = score;
                _score.text = score.ToString("N0");
            }
            if (combo != _shownCombo)
            {
                _shownCombo = combo;
                _combo.text = combo >= 2 ? Loc.F("race.combo", combo, multiplier.ToString("0.0")) : string.Empty;
            }
        }

        public void SetProgress(float progress) => _progress.fillAmount = Mathf.Clamp01(progress);

        public void SetSpirit(float spirit01, bool ready, float activeFraction)
        {
            if (_skillRoot == null) return;
            _spiritFill.fillAmount = spirit01;
            _spiritFill.color = ready ? UiTheme.Accent : UiTheme.Primary;
            bool glow = ready && activeFraction <= 0f;
            if (_skillGlow.enabled != glow) _skillGlow.enabled = glow;
            if (glow) _skillGlow.color = UiTheme.Accent.WithAlpha(0.45f + 0.35f * Mathf.Sin(Time.unscaledTime * 6f));
            bool active = activeFraction > 0f;
            if (_activeRing.enabled != active) _activeRing.enabled = active;
            if (active) _activeRing.fillAmount = activeFraction;
        }

        public void Judge(Judgement judgement, bool big, bool shielded)
        {
            string key;
            Color color;
            switch (judgement)
            {
                case Judgement.Perfect: key = big ? "judge.perfectBig" : "judge.perfect"; color = UiTheme.Accent; break;
                case Judgement.Good: key = "judge.good"; color = UiTheme.PrimaryLight; break;
                case Judgement.HazardDodge: key = "judge.dodge"; color = UiTheme.Hex("#7CFFB2"); break;
                case Judgement.HazardHit: key = "judge.hit"; color = UiTheme.Bad; break;
                default: key = "judge.miss"; color = UiTheme.InkFaint; break;
            }
            if (shielded) key = "judge.shield";
            int i = _nextJudge;
            _nextJudge = (_nextJudge + 1) % _judgements.Length;
            _judgements[i].text = Loc.T(key);
            _judgements[i].color = color;
            _judgements[i].enabled = true;
            _judgements[i].transform.SetAsLastSibling();
            _judgeAge[i] = 0f;
        }

        public void SetCountdown(string text)
        {
            _countdown.text = text ?? string.Empty;
            _countdown.enabled = !string.IsNullOrEmpty(text);
            _countdown.rectTransform.localScale = Vector3.one * 1.25f;
        }

        public void Bark(string text, float seconds = 3.2f)
        {
            if (_bubble == null || string.IsNullOrEmpty(text)) return;
            _bubbleText.text = text;
            _bubble.gameObject.SetActive(true);
            _bubble.localScale = new Vector3(0.9f, 0.9f, 1f);
            _bubbleUntil = Time.unscaledTime + seconds;
        }

        public void ShowPause(bool visible)
        {
            _pausePanel.gameObject.SetActive(visible);
            if (visible) _pausePanel.SetAsLastSibling();
        }

        public void SkillCutIn()
        {
            if (_cutIn == null) return;
            StopAllCoroutines();
            StartCoroutine(CutInRoutine());
        }

        IEnumerator CutInRoutine()
        {
            _cutIn.gameObject.SetActive(true);
            const float inTime = 0.18f, hold = 0.7f, outTime = 0.2f;
            for (float t = 0f; t < inTime + hold + outTime; t += Time.unscaledDeltaTime)
            {
                float x;
                if (t < inTime) x = Mathf.Lerp(-2400f, 0f, 1f - Mathf.Pow(1f - t / inTime, 3f));
                else if (t < inTime + hold) x = Mathf.Lerp(0f, 60f, (t - inTime) / hold);
                else x = Mathf.Lerp(60f, 2600f, Mathf.Pow((t - inTime - hold) / outTime, 2f));
                _cutIn.anchoredPosition = new Vector2(x, 260f);
                yield return null;
            }
            _cutIn.gameObject.SetActive(false);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            for (int i = 0; i < _judgements.Length; i++)
            {
                if (!_judgements[i].enabled) continue;
                _judgeAge[i] += dt;
                float age = _judgeAge[i];
                if (age > 0.55f)
                {
                    _judgements[i].enabled = false;
                    continue;
                }
                _judgeRt[i].anchoredPosition = new Vector2(0f, 150f + age * 120f);
                float s = age < 0.08f ? Mathf.Lerp(1.4f, 1f, age / 0.08f) : 1f;
                _judgeRt[i].localScale = new Vector3(s, s, 1f);
                var c = _judgements[i].color;
                c.a = Mathf.Clamp01(1f - (age - 0.3f) / 0.25f);
                _judgements[i].color = c;
            }
            if (_countdown.enabled)
                _countdown.rectTransform.localScale = Vector3.Lerp(_countdown.rectTransform.localScale, Vector3.one, dt * 10f);
            if (_bubble != null && _bubble.gameObject.activeSelf)
            {
                _bubble.localScale = Vector3.Lerp(_bubble.localScale, Vector3.one, dt * 14f);
                if (Time.unscaledTime > _bubbleUntil) _bubble.gameObject.SetActive(false);
            }
        }
    }

    /// <summary>Picks navigator voice lines for race events (bond-gated, rate-limited) like Riichi City characters.</summary>
    public sealed class NavigatorBarks
    {
        readonly NavigatorDef _navigator;
        readonly int _bondLevel;
        readonly RaceHud _hud;
        readonly bool _enabled;
        float _nextAllowed;

        public NavigatorBarks(NavigatorDef navigator, int bondLevel, RaceHud hud, bool enabled)
        {
            _navigator = navigator;
            _bondLevel = bondLevel;
            _hud = hud;
            _enabled = enabled;
        }

        public void Say(string trigger, bool important = false)
        {
            if (!_enabled || _navigator == null) return;
            float now = Time.unscaledTime;
            if (!important && now < _nextAllowed) return;
            var line = Pick(trigger);
            if (line == null && (trigger == VoiceTriggers.FinishMid)) line = Pick(VoiceTriggers.FinishHigh);
            if (line == null) return;
            _nextAllowed = now + 7f;
            _hud.Bark(Loc.Pick(line.text));
            var clip = NavigatorArt.Voice(_navigator.id, line.id);
            if (clip != null) GameApp.Instance.Audio.Voice(clip);
        }

        VoiceLine Pick(string trigger)
        {
            VoiceLine chosen = null;
            int seen = 0;
            foreach (var line in _navigator.LinesFor(trigger, _bondLevel))
            {
                seen++;
                if (UnityEngine.Random.Range(0, seen) == 0) chosen = line;
            }
            return chosen;
        }
    }
}

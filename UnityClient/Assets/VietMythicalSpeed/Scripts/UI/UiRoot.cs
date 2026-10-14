using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VMS.Backend;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem.UI;
#endif

namespace VMS.UI
{
    /// <summary>Base class for full-screen menus. Screens are rebuilt from code each time they are shown.</summary>
    public abstract class UiScreen : MonoBehaviour
    {
        protected GameApp App => GameApp.Instance;
        protected RectTransform Root { get; private set; }

        internal void Setup(RectTransform root, object arg)
        {
            Root = root;
            Build(arg);
        }

        protected abstract void Build(object arg);

        /// <summary>Android back / Escape. Return true when handled; false lets the router go back.</summary>
        public virtual bool OnBack() => false;

        public virtual void OnProfileChanged() { }
    }

    /// <summary>Canvases, screen navigation and overlays. One instance, owned by <see cref="GameApp"/>.</summary>
    public sealed class UiRoot : MonoBehaviour
    {
        public static UiRoot Instance { get; private set; }

        public RectTransform ScreenLayer { get; private set; }
        public RectTransform HudLayer { get; private set; }
        public RectTransform PopupLayer { get; private set; }
        public RectTransform ToastLayer { get; private set; }
        public LoadingOverlay Loading { get; private set; }

        Canvas _screenCanvas, _hudCanvas;
        UiScreen _current;
        Type _currentType;
        object _currentArg;
        readonly Stack<KeyValuePair<Type, object>> _history = new Stack<KeyValuePair<Type, object>>();
        readonly List<Popup> _popups = new List<Popup>();

        public UiScreen Current => _current;

        public static UiRoot Create(Transform parent)
        {
            var go = new GameObject("UI");
            go.transform.SetParent(parent, false);
            var root = go.AddComponent<UiRoot>();
            root.Build();
            return root;
        }

        void OnDestroy()
        {
            Loc.LanguageChanged -= RefreshFonts;
        }

        /// <summary>Text built before a language change (toasts, loading, open popups) switches to the new font.</summary>
        void RefreshFonts()
        {
            var font = UiTheme.Font;
            foreach (var t in GetComponentsInChildren<Text>(true)) t.font = font;
        }

        void Build()
        {
            Instance = this;
            Loc.LanguageChanged += RefreshFonts;
            EnsureEventSystem();
            _screenCanvas = MakeCanvas("Screens", 0, out var screens);
            _hudCanvas = MakeCanvas("HUD", 10, out var hud);
            MakeCanvas("Popups", 50, out var popups);
            MakeCanvas("Toasts", 60, out var toasts);
            MakeCanvas("Loading", 70, out var loading);
            ScreenLayer = screens;
            HudLayer = hud;
            PopupLayer = popups;
            ToastLayer = toasts;
            Loading = LoadingOverlay.Create(loading);
            _hudCanvas.enabled = false;
        }

        Canvas MakeCanvas(string name, int order, out RectTransform safeContent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(transform, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = order;
            canvas.pixelPerfect = false;
            canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.None;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            go.AddComponent<GraphicRaycaster>();
            safeContent = Ui.Node("SafeArea", go.transform).Fill();
            safeContent.gameObject.AddComponent<SafeArea>();
            return canvas;
        }

        static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            var es = new GameObject("EventSystem");
            DontDestroyOnLoad(es);
            // No axis/button-driven Move/Submit/Cancel: the UI is touch/mouse-first and Back/Escape is handled in
            // Update. Laptop sensors that enumerate as joysticks (e.g. HP DriveGuard accelerometers) otherwise
            // drift the selection and "press" buttons on their own.
            es.AddComponent<EventSystem>().sendNavigationEvents = false;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            es.AddComponent<InputSystemUIInputModule>();
#else
            es.AddComponent<StandaloneInputModule>();
#endif
        }

        // ------------------------------------------------------------------ screens

        public T Show<T>(object arg = null, bool remember = true) where T : UiScreen
        {
            if (remember && _current != null && _currentType != typeof(T))
                _history.Push(new KeyValuePair<Type, object>(_currentType, _currentArg));
            return (T)Open(typeof(T), arg);
        }

        /// <summary>Clears the back stack (e.g. after signing in) and shows <typeparamref name="T"/>.</summary>
        public T Reset<T>(object arg = null) where T : UiScreen
        {
            _history.Clear();
            return (T)Open(typeof(T), arg);
        }

        public void Back()
        {
            if (_history.Count == 0) return;
            var prev = _history.Pop();
            Open(prev.Key, prev.Value);
        }

        UiScreen Open(Type type, object arg)
        {
            if (_current != null) Destroy(_current.gameObject);
            if (Debug.isDebugBuild) Debug.Log("[UI] Show " + type.Name + " (history " + _history.Count + ")");
            var rt = Ui.Node(type.Name, ScreenLayer).Fill();
            var group = rt.gameObject.AddComponent<CanvasGroup>();
            _current = (UiScreen)rt.gameObject.AddComponent(type);
            _currentType = type;
            _currentArg = arg;
            try
            {
                _current.Setup(rt, arg);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            StartCoroutine(FadeIn(group, rt));
            return _current;
        }

        static IEnumerator FadeIn(CanvasGroup group, RectTransform rt)
        {
            const float duration = 0.2f;
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                if (group == null) yield break;
                float k = t / duration;
                group.alpha = k;
                rt.anchoredPosition = new Vector2(0f, -24f * (1f - k) * (1f - k));
                yield return null;
            }
            if (group == null) yield break;
            group.alpha = 1f;
            rt.anchoredPosition = Vector2.zero;
        }

        /// <summary>Menus are hidden (and cost nothing to render) while racing.</summary>
        public void SetMenusVisible(bool visible)
        {
            _screenCanvas.enabled = visible;
            _hudCanvas.enabled = !visible;
        }

        public void NotifyProfileChanged()
        {
            if (_current != null) _current.OnProfileChanged();
        }

        // ------------------------------------------------------------------ overlays

        internal void Register(Popup p) => _popups.Add(p);
        internal void Unregister(Popup p) => _popups.Remove(p);

        public void Toast(string text, float seconds = 2.6f) => StartCoroutine(ToastRoutine(text, seconds));

        IEnumerator ToastRoutine(string text, float seconds)
        {
            var rt = Ui.Node("Toast", ToastLayer).At(0.5f, 1f, 0f, -150f, 980f, 90f, 0.5f, 1f);
            var group = rt.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            var bg = Ui.Image(rt, UiSprites.Rounded, new Color(0.13f, 0.19f, 0.31f, 0.92f), "Bg");
            bg.rectTransform.Fill();
            var label = Ui.FitLabel(rt, text, 32, Color.white, TextAnchor.MiddleCenter, FontStyle.Normal);
            label.rectTransform.Fill(30f, 6f, 30f, 6f);
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                group.alpha = Mathf.Min(1f, Mathf.Min(t / 0.15f, (seconds - t) / 0.3f));
                yield return null;
            }
            Destroy(rt.gameObject);
        }

        /// <summary>Runs async work behind the loading overlay and reports failures as a message.</summary>
        public async Task<bool> Busy(string text, Func<Task> work)
        {
            Loading.Show(text);
            try
            {
                await work();
                return true;
            }
            catch (BackendException e)
            {
                // An outdated client already sees the blocking update dialog.
                if (e.Code != "cancelled" && !(e is ClientOutdatedException)) Popup.Message(Loc.T("common.error"), e.Friendly);
                return false;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Popup.Message(Loc.T("common.error"), e.Message);
                return false;
            }
            finally
            {
                Loading.Hide();
            }
        }

        void Update()
        {
            if (!InputCompat.BackPressed()) return;
            if (Loading.Visible) return;
            if (_popups.Count > 0)
            {
                _popups[_popups.Count - 1].RequestClose();
                return;
            }
            if (_current != null && !_current.OnBack()) Back();
        }
    }

    public static class InputCompat
    {
        public static bool BackPressed()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.Escape);
#else
            var kb = UnityEngine.InputSystem.Keyboard.current;
            return kb != null && kb.escapeKey.wasPressedThisFrame;
#endif
        }
    }

    /// <summary>Modal dialog on a dimmed backdrop.</summary>
    public sealed class Popup : MonoBehaviour
    {
        public RectTransform Body { get; private set; }
        public RectTransform Card { get; private set; }
        public event Action Closed;
        bool _closable;

        public static Popup Open(string title, float width, float height, bool closable = true)
        {
            var layer = UiRoot.Instance.PopupLayer;
            var rt = Ui.Node("Popup", layer).Fill();
            var popup = rt.gameObject.AddComponent<Popup>();
            popup._closable = closable;
            var scrim = Ui.Image(rt, UiSprites.White, UiTheme.Scrim, "Scrim");
            scrim.rectTransform.Fill(-200f, -200f, -200f, -200f);
            scrim.raycastTarget = true;
            if (closable) Ui.AddClick(scrim.gameObject, popup.RequestClose);

            var card = Ui.Card(rt, UiTheme.Panel, "Card");
            popup.Card = card.rectTransform;
            popup.Card.At(0.5f, 0.5f, 0f, 0f, width, height);
            var band = Ui.Skew(popup.Card, UiTheme.Hex("#4FB0FF"), UiTheme.Primary, 30f, "TitleBand");
            band.rectTransform.At(0f, 1f, 40f, 30f, Mathf.Min(width - 80f, 620f), 76f, 0f, 1f);
            var titleLabel = Ui.FitLabel(band.rectTransform, title, 38, Color.white, TextAnchor.MiddleLeft);
            titleLabel.rectTransform.Fill(34f, 2f, 24f, 2f);
            if (closable)
            {
                var close = Ui.RoundButton(popup.Card, "✕", popup.RequestClose, UiTheme.PanelAlt, UiTheme.InkSoft, "Close");
                close.GetComponent<RectTransform>().At(1f, 1f, -24f, -24f, 70f, 70f, 1f, 1f);
            }
            popup.Body = Ui.Node("Body", popup.Card).Fill(40f, 80f, 40f, 34f);
            UiRoot.Instance.Register(popup);
            popup.StartCoroutine(popup.Pop());
            return popup;
        }

        IEnumerator Pop()
        {
            for (float t = 0f; t < 0.16f; t += Time.unscaledDeltaTime)
            {
                float k = t / 0.16f;
                Card.localScale = Vector3.one * Mathf.Lerp(0.92f, 1f, 1f - (1f - k) * (1f - k));
                yield return null;
            }
            Card.localScale = Vector3.one;
        }

        public void RequestClose()
        {
            if (_closable) Close();
        }

        public void Close()
        {
            if (this == null) return;
            UiRoot.Instance.Unregister(this);
            Closed?.Invoke();
            Destroy(gameObject);
        }

        public static Popup Message(string title, string message)
        {
            var p = Open(title, 980f, 520f);
            var text = Ui.Label(p.Body, message, UiTheme.BodySize, UiTheme.Ink, TextAnchor.UpperLeft);
            text.rectTransform.Fill(0f, 0f, 0f, 110f);
            var ok = Ui.Button(p.Body, Loc.T("common.ok"), p.Close, ButtonStyle.Primary);
            ok.GetComponent<RectTransform>().At(0.5f, 0f, 0f, 0f, 320f, 90f, 0.5f, 0f);
            return p;
        }

        public static Popup Confirm(string title, string message, string okLabel, Action onOk, string cancelLabel = null, ButtonStyle okStyle = ButtonStyle.Accent)
        {
            var p = Open(title, 1040f, 560f);
            var text = Ui.Label(p.Body, message, UiTheme.BodySize, UiTheme.Ink, TextAnchor.UpperLeft);
            text.rectTransform.Fill(0f, 0f, 0f, 120f);
            var cancel = Ui.Button(p.Body, cancelLabel ?? Loc.T("common.cancel"), p.Close, ButtonStyle.Secondary);
            cancel.GetComponent<RectTransform>().At(0.5f, 0f, -190f, 0f, 340f, 90f, 0.5f, 0f);
            var ok = Ui.Button(p.Body, okLabel, () =>
            {
                p.Close();
                onOk?.Invoke();
            }, okStyle);
            ok.GetComponent<RectTransform>().At(0.5f, 0f, 190f, 0f, 340f, 90f, 0.5f, 0f);
            return p;
        }
    }

    /// <summary>
    /// Full-screen busy indicator. <see cref="Report"/> is thread-safe (song analysis reports from a worker
    /// thread); the UI picks the values up in Update.
    /// </summary>
    public sealed class LoadingOverlay : MonoBehaviour
    {
        Text _label, _detail;
        Image _bar;
        RectTransform _barRoot;
        Button _cancel;
        Action _onCancel;
        readonly object _lock = new object();
        string _stage;
        float _progress = -1f;
        bool _dirty;

        public bool Visible => gameObject.activeSelf;

        public static LoadingOverlay Create(RectTransform layer)
        {
            var rt = Ui.Node("LoadingOverlay", layer).Fill();
            var overlay = rt.gameObject.AddComponent<LoadingOverlay>();
            var scrim = Ui.Image(rt, UiSprites.White, UiTheme.Scrim, "Scrim");
            scrim.rectTransform.Fill(-200f, -200f, -200f, -200f);
            scrim.raycastTarget = true;

            var halo = Ui.Image(rt, UiSprites.Halo, UiTheme.Accent, "Halo");
            halo.rectTransform.At(0.5f, 0.5f, 0f, 120f, 220f, 220f);
            var spin = halo.gameObject.AddComponent<Spin>();
            spin.degreesPerSecond = 90f;
            spin.essential = true;
            var ring = Ui.Image(rt, UiSprites.Ring, Color.white.WithAlpha(0.8f), "Ring");
            ring.rectTransform.At(0.5f, 0.5f, 0f, 120f, 120f, 120f);

            overlay._label = Ui.Label(rt, string.Empty, 40, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold, "Text");
            overlay._label.rectTransform.At(0.5f, 0.5f, 0f, -40f, 1400f, 70f);
            overlay._detail = Ui.Label(rt, string.Empty, 28, new Color(1f, 1f, 1f, 0.8f), TextAnchor.MiddleCenter, FontStyle.Normal, "Detail");
            overlay._detail.rectTransform.At(0.5f, 0.5f, 0f, -100f, 1400f, 50f);

            overlay._barRoot = Ui.Node("Progress", rt).At(0.5f, 0.5f, 0f, -160f, 760f, 16f);
            Ui.Image(overlay._barRoot, UiSprites.RoundedSmall, new Color(1f, 1f, 1f, 0.25f), "Track").rectTransform.Fill();
            overlay._bar = Ui.Image(overlay._barRoot, UiSprites.White, UiTheme.Accent, "Fill");
            overlay._bar.type = Image.Type.Filled;
            overlay._bar.fillMethod = Image.FillMethod.Horizontal;
            overlay._bar.rectTransform.Fill();

            overlay._cancel = Ui.Button(rt, Loc.T("common.cancel"), overlay.CancelPressed, ButtonStyle.Secondary, 30);
            overlay._cancel.GetComponent<RectTransform>().At(0.5f, 0.5f, 0f, -270f, 300f, 80f);
            rt.gameObject.SetActive(false);
            return overlay;
        }

        public void Show(string text, Action onCancel = null)
        {
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            _label.text = text;
            _detail.text = string.Empty;
            _onCancel = onCancel;
            _cancel.gameObject.SetActive(onCancel != null);
            lock (_lock)
            {
                _stage = null;
                _progress = -1f;
                _dirty = true;
            }
        }

        public void SetText(string text) => _label.text = text;

        /// <summary>Thread-safe progress report (stage is a localisation key, progress 0..1 or negative to hide).</summary>
        public void Report(string stageKey, float progress)
        {
            lock (_lock)
            {
                _stage = stageKey;
                _progress = progress;
                _dirty = true;
            }
        }

        public void Hide()
        {
            _onCancel = null;
            gameObject.SetActive(false);
        }

        void CancelPressed()
        {
            var cb = _onCancel;
            Hide();
            cb?.Invoke();
        }

        void Update()
        {
            string stage;
            float progress;
            lock (_lock)
            {
                if (!_dirty) return;
                _dirty = false;
                stage = _stage;
                progress = _progress;
            }
            _detail.text = string.IsNullOrEmpty(stage) ? string.Empty : Loc.T(stage);
            _barRoot.gameObject.SetActive(progress >= 0f);
            _bar.fillAmount = Mathf.Clamp01(progress);
        }
    }
}

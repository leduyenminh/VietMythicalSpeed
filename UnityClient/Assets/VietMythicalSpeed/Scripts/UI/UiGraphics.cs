using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VMS.UI
{
    /// <summary>Parallelogram with an optional vertical gradient: the slanted Blue Archive button/banner shape.</summary>
    public sealed class SkewQuad : MaskableGraphic
    {
        /// <summary>Horizontal offset of the top edge relative to the bottom, in pixels.</summary>
        public float skew = 22f;
        public Color topColor = Color.white;
        public Color bottomColor = Color.white;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = GetPixelAdjustedRect();
            float s = skew * 0.5f;
            Color top = topColor * color, bottom = bottomColor * color;
            if (UiTheme.Flat) top = bottom = Color.Lerp(top, bottom, 0.5f);
            vh.AddVert(new Vector3(r.xMin - s, r.yMin), bottom, Vector2.zero);
            vh.AddVert(new Vector3(r.xMin + s, r.yMax), top, Vector2.up);
            vh.AddVert(new Vector3(r.xMax + s, r.yMax), top, Vector2.one);
            vh.AddVert(new Vector3(r.xMax - s, r.yMin), bottom, Vector2.right);
            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(0, 2, 3);
        }

        public void SetColors(Color top, Color bottom)
        {
            topColor = top;
            bottomColor = bottom;
            SetVerticesDirty();
        }
    }

    /// <summary>Rectangle with four corner colours (backgrounds, banner washes).</summary>
    public sealed class GradientRect : MaskableGraphic
    {
        public Color topLeft = Color.white, topRight = Color.white, bottomLeft = Color.white, bottomRight = Color.white;

        public void Vertical(Color top, Color bottom)
        {
            topLeft = topRight = top;
            bottomLeft = bottomRight = bottom;
            SetVerticesDirty();
        }

        public void Diagonal(Color a, Color b)
        {
            topLeft = a;
            bottomRight = b;
            topRight = bottomLeft = Color.Lerp(a, b, 0.5f);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = GetPixelAdjustedRect();
            Color bl = bottomLeft, tl = topLeft, tr = topRight, br = bottomRight;
            if (UiTheme.Flat) bl = tl = tr = br = (bl + tl + tr + br) * 0.25f;
            vh.AddVert(new Vector3(r.xMin, r.yMin), bl * color, Vector2.zero);
            vh.AddVert(new Vector3(r.xMin, r.yMax), tl * color, Vector2.up);
            vh.AddVert(new Vector3(r.xMax, r.yMax), tr * color, Vector2.one);
            vh.AddVert(new Vector3(r.xMax, r.yMin), br * color, Vector2.right);
            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(0, 2, 3);
        }
    }

    /// <summary>Keeps its RectTransform inside Screen.safeArea (notches, rounded corners, gesture bars).</summary>
    public sealed class SafeArea : MonoBehaviour
    {
        Rect _applied;
        Vector2Int _screen;

        void OnEnable() => Apply();

        void Update()
        {
            if (Screen.safeArea != _applied || _screen.x != Screen.width || _screen.y != Screen.height) Apply();
        }

        void Apply()
        {
            var rt = (RectTransform)transform;
            var area = Screen.safeArea;
            _applied = area;
            _screen = new Vector2Int(Screen.width, Screen.height);
            if (Screen.width <= 0 || Screen.height <= 0) return;
            rt.anchorMin = new Vector2(area.xMin / Screen.width, area.yMin / Screen.height);
            rt.anchorMax = new Vector2(area.xMax / Screen.width, area.yMax / Screen.height);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }

    /// <summary>Squash-on-press feedback plus the tap sound for any selectable.</summary>
    public sealed class PressFeedback : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public float pressedScale = 0.95f;
        public bool sound = true;
        Vector3 _baseScale = Vector3.one;
        bool _pressed;

        void Awake() => _baseScale = transform.localScale;

        public void OnPointerDown(PointerEventData eventData)
        {
            var selectable = GetComponent<Selectable>();
            if (selectable != null && !selectable.IsInteractable()) return;
            _pressed = true;
            transform.localScale = _baseScale * pressedScale;
            if (sound && GameApp.Instance != null) GameApp.Instance.Audio.Tap();
        }

        public void OnPointerUp(PointerEventData eventData) => Release();
        public void OnPointerExit(PointerEventData eventData) => Release();

        void Release()
        {
            if (!_pressed) return;
            _pressed = false;
            transform.localScale = _baseScale;
        }
    }

    /// <summary>Gentle idle motion for decorative elements (floating clouds, the lobby navigator's breathing).</summary>
    public sealed class Bob : MonoBehaviour
    {
        public Vector2 amplitude = new Vector2(0f, 8f);
        public float speed = 1.2f;
        public float phase;
        public float scalePulse;
        Vector2 _origin;
        Vector3 _scale;
        RectTransform _rt;

        void Start()
        {
            _rt = (RectTransform)transform;
            _origin = _rt.anchoredPosition;
            _scale = _rt.localScale;
            if (UiTheme.Flat) enabled = false; // flat UI: decorations hold still
        }

        void Update()
        {
            float t = Time.unscaledTime * speed + phase;
            _rt.anchoredPosition = _origin + new Vector2(Mathf.Sin(t * 0.7f) * amplitude.x, Mathf.Sin(t) * amplitude.y);
            if (scalePulse > 0f) _rt.localScale = _scale * (1f + Mathf.Sin(t * 0.8f) * scalePulse);
        }
    }

    /// <summary>Slowly rotates a graphic (gacha rays, skill-ready ring).</summary>
    public sealed class Spin : MonoBehaviour
    {
        public float degreesPerSecond = 20f;
        /// <summary>Keeps spinning in flat UI (busy indicators); decorative spins stop.</summary>
        public bool essential;

        void Start()
        {
            if (UiTheme.Flat && !essential) enabled = false;
        }

        void Update() => transform.Rotate(0f, 0f, -degreesPerSecond * Time.unscaledDeltaTime);
    }
}

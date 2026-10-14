using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace VMS.UI
{
    public enum ButtonStyle
    {
        /// <summary>Blue slanted button, white text.</summary>
        Primary,
        /// <summary>White slanted button, blue text.</summary>
        Secondary,
        /// <summary>Yellow slanted button, ink text – the main call to action.</summary>
        Accent,
        /// <summary>Red lacquer button for destructive actions.</summary>
        Danger,
        /// <summary>Flat text button.</summary>
        Ghost,
    }

    /// <summary>
    /// Code-first uGUI builders. Every screen is assembled from these, so the project needs no prefabs.
    /// Non-interactive graphics never receive raycasts (cheaper event processing on mobile).
    /// </summary>
    public static class Ui
    {
        // ------------------------------------------------------------------ layout

        public static RectTransform Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        /// <summary>Stretch to the parent with margins (left, top, right, bottom).</summary>
        public static RectTransform Fill(this RectTransform rt, float left = 0f, float top = 0f, float right = 0f, float bottom = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        /// <summary>Fixed-size box anchored at a point of the parent (0..1 coordinates).</summary>
        public static RectTransform At(this RectTransform rt, float ax, float ay, float x, float y, float width, float height, float px = 0.5f, float py = 0.5f)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(ax, ay);
            rt.pivot = new Vector2(px, py);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(width, height);
            return rt;
        }

        /// <summary>Anchored region of the parent (0..1) with pixel insets.</summary>
        public static RectTransform Region(this RectTransform rt, float minX, float minY, float maxX, float maxY, float inset = 0f)
        {
            rt.anchorMin = new Vector2(minX, minY);
            rt.anchorMax = new Vector2(maxX, maxY);
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
            return rt;
        }

        public static LayoutElement Layout(this Component c, float width = -1f, float height = -1f, float flexWidth = -1f, float flexHeight = -1f)
        {
            var le = c.gameObject.GetComponent<LayoutElement>() ?? c.gameObject.AddComponent<LayoutElement>();
            if (width >= 0f) le.preferredWidth = le.minWidth = width;
            if (height >= 0f) le.preferredHeight = le.minHeight = height;
            if (flexWidth >= 0f) le.flexibleWidth = flexWidth;
            if (flexHeight >= 0f) le.flexibleHeight = flexHeight;
            return le;
        }

        public static VerticalLayoutGroup VStack(RectTransform rt, float spacing, int padding = 0, TextAnchor align = TextAnchor.UpperCenter, bool expandWidth = true)
        {
            var g = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            g.spacing = spacing;
            g.padding = new RectOffset(padding, padding, padding, padding);
            g.childAlignment = align;
            g.childControlWidth = expandWidth;
            g.childControlHeight = true;
            g.childForceExpandWidth = expandWidth;
            g.childForceExpandHeight = false;
            return g;
        }

        public static HorizontalLayoutGroup HStack(RectTransform rt, float spacing, int padding = 0, TextAnchor align = TextAnchor.MiddleLeft)
        {
            var g = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            g.spacing = spacing;
            g.padding = new RectOffset(padding, padding, padding, padding);
            g.childAlignment = align;
            g.childControlWidth = true;
            g.childControlHeight = true;
            g.childForceExpandWidth = false;
            g.childForceExpandHeight = false;
            return g;
        }

        // ------------------------------------------------------------------ graphics

        public static Image Image(Transform parent, Sprite sprite, Color color, string name = "Image")
        {
            var rt = Node(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            if (sprite != null && sprite.border != Vector4.zero) img.type = UnityEngine.UI.Image.Type.Sliced;
            return img;
        }

        /// <summary>Rounded white card with a soft drop shadow.</summary>
        public static Image Card(Transform parent, Color color, string name = "Card", bool shadow = true)
        {
            var rt = Node(name, parent);
            if (shadow && !UiTheme.Flat)
            {
                var sh = Image(rt, UiSprites.Rounded, new Color(0.1f, 0.25f, 0.5f, 0.12f), "Shadow");
                sh.rectTransform.Fill(-4f, 2f, -4f, -10f);
            }
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = UiSprites.Rounded;
            img.type = UnityEngine.UI.Image.Type.Sliced;
            img.color = color;
            img.raycastTarget = true;
            return img;
        }

        public static GradientRect Gradient(Transform parent, Color top, Color bottom, string name = "Gradient")
        {
            var rt = Node(name, parent);
            var g = rt.gameObject.AddComponent<GradientRect>();
            g.Vertical(top, bottom);
            g.raycastTarget = false;
            return g;
        }

        public static SkewQuad Skew(Transform parent, Color top, Color bottom, float skew = 22f, string name = "Skew")
        {
            var rt = Node(name, parent);
            var q = rt.gameObject.AddComponent<SkewQuad>();
            q.skew = skew;
            q.SetColors(top, bottom);
            q.raycastTarget = false;
            return q;
        }

        public static Text Label(Transform parent, string text, int size, Color color, TextAnchor align = TextAnchor.MiddleLeft,
            FontStyle style = FontStyle.Normal, string name = "Label")
        {
            var rt = Node(name, parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = UiTheme.Font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.fontStyle = style;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = true;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>Label that shrinks to fit its box (long Vietnamese titles).</summary>
        public static Text FitLabel(Transform parent, string text, int maxSize, Color color, TextAnchor align = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Bold)
        {
            var t = Label(parent, text, maxSize, color, align, style);
            t.resizeTextForBestFit = true;
            t.resizeTextMaxSize = maxSize;
            t.resizeTextMinSize = Mathf.Max(14, maxSize / 2);
            t.verticalOverflow = VerticalWrapMode.Truncate;
            return t;
        }

        // ------------------------------------------------------------------ controls

        public static Button Button(Transform parent, string label, Action onClick, ButtonStyle style = ButtonStyle.Primary, int fontSize = 34, string name = null)
        {
            var rt = Node(name ?? "Button " + label, parent);
            Color top, bottom, text;
            switch (style)
            {
                case ButtonStyle.Secondary: top = Color.white; bottom = UiTheme.PanelAlt; text = UiTheme.PrimaryDark; break;
                case ButtonStyle.Accent: top = UiTheme.Hex("#FFE27A"); bottom = UiTheme.Accent; text = UiTheme.Ink; break;
                case ButtonStyle.Danger: top = UiTheme.Hex("#EE4A5C"); bottom = UiTheme.Lacquer; text = Color.white; break;
                case ButtonStyle.Ghost: top = bottom = new Color(1f, 1f, 1f, 0f); text = UiTheme.PrimaryDark; break;
                default: top = UiTheme.Hex("#4FB0FF"); bottom = UiTheme.Primary; text = Color.white; break;
            }

            if (style != ButtonStyle.Ghost && !UiTheme.Flat)
            {
                var shadow = Skew(rt, new Color(0.08f, 0.2f, 0.45f, 0.25f), new Color(0.08f, 0.2f, 0.45f, 0.25f), 22f, "Shadow");
                shadow.rectTransform.Fill(2f, 6f, -2f, -6f);
            }
            var body = Skew(rt, top, bottom, 22f, "Body");
            body.rectTransform.Fill();
            body.raycastTarget = true;
            if (style == ButtonStyle.Secondary)
            {
                var stripe = Skew(body.rectTransform, UiTheme.Primary, UiTheme.Primary, 6f, "Stripe");
                stripe.rectTransform.anchorMin = new Vector2(0f, 0f);
                stripe.rectTransform.anchorMax = new Vector2(1f, 0f);
                stripe.rectTransform.offsetMin = new Vector2(8f, 0f);
                stripe.rectTransform.offsetMax = new Vector2(-8f, 6f);
            }

            var lbl = FitLabel(rt, label, fontSize, text, TextAnchor.MiddleCenter);
            lbl.rectTransform.Fill(16f, 4f, 16f, 4f);

            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = body;
            button.transition = Selectable.Transition.ColorTint;
            var colors = button.colors;
            colors.disabledColor = new Color(0.75f, 0.75f, 0.78f, 0.6f);
            colors.pressedColor = new Color(0.9f, 0.9f, 0.95f, 1f);
            button.colors = colors;
            if (onClick != null) button.onClick.AddListener(() => onClick());
            rt.gameObject.AddComponent<PressFeedback>();
            return button;
        }

        public static void SetLabel(this Button b, string text)
        {
            var t = b.GetComponentInChildren<Text>();
            if (t != null) t.text = text;
        }

        /// <summary>Round icon-style button (pause, close, back) showing a glyph.</summary>
        public static Button RoundButton(Transform parent, string glyph, Action onClick, Color background, Color foreground, string name = "RoundButton")
        {
            var rt = Node(name, parent);
            var bg = rt.gameObject.AddComponent<Image>();
            bg.sprite = UiSprites.Circle;
            bg.color = background;
            var t = Label(rt, glyph, 40, foreground, TextAnchor.MiddleCenter, FontStyle.Bold);
            t.rectTransform.Fill();
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = bg;
            if (onClick != null) b.onClick.AddListener(() => onClick());
            rt.gameObject.AddComponent<PressFeedback>();
            return b;
        }

        public static InputField Input(Transform parent, string placeholder, InputField.ContentType type, string initial = "")
        {
            var rt = Node("Input " + placeholder, parent);
            var bg = rt.gameObject.AddComponent<Image>();
            bg.sprite = UiSprites.RoundedSmall;
            bg.type = UnityEngine.UI.Image.Type.Sliced;
            bg.color = UiTheme.PanelAlt;

            var outline = Image(rt, UiSprites.RoundedSmallOutline, UiTheme.Line, "Outline");
            outline.rectTransform.Fill();

            var ph = Label(rt, placeholder, UiTheme.BodySize - 2, UiTheme.InkFaint, TextAnchor.MiddleLeft, FontStyle.Italic, "Placeholder");
            ph.rectTransform.Fill(24f, 6f, 24f, 6f);
            var text = Label(rt, string.Empty, UiTheme.BodySize, UiTheme.Ink, TextAnchor.MiddleLeft, FontStyle.Normal, "Text");
            text.rectTransform.Fill(24f, 6f, 24f, 6f);
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;

            var field = rt.gameObject.AddComponent<InputField>();
            field.targetGraphic = bg;
            field.textComponent = text;
            field.placeholder = ph;
            field.contentType = type;
            field.lineType = InputField.LineType.SingleLine;
            field.text = initial ?? string.Empty;
            return field;
        }

        public static Slider Slider(Transform parent, float min, float max, float value, Action<float> onChange, bool wholeNumbers = false)
        {
            var rt = Node("Slider", parent);
            var track = Image(rt, UiSprites.RoundedSmall, UiTheme.Line, "Track");
            track.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            track.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            track.rectTransform.sizeDelta = new Vector2(0f, 14f);

            var fillArea = Node("Fill Area", rt);
            fillArea.anchorMin = new Vector2(0f, 0.5f);
            fillArea.anchorMax = new Vector2(1f, 0.5f);
            fillArea.sizeDelta = new Vector2(0f, 14f);
            var fill = Image(fillArea, UiSprites.RoundedSmall, UiTheme.Primary, "Fill");
            fill.rectTransform.sizeDelta = Vector2.zero;

            var handleArea = Node("Handle Area", rt).Fill(18f, 0f, 18f, 0f);
            var handle = Image(handleArea, UiSprites.Circle, Color.white, "Handle");
            handle.rectTransform.sizeDelta = new Vector2(44f, 44f);
            var ring = Image(handle.rectTransform, UiSprites.Ring, UiTheme.Primary, "Ring");
            ring.rectTransform.Fill();

            var slider = rt.gameObject.AddComponent<Slider>();
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = wholeNumbers;
            slider.value = value;
            handle.raycastTarget = true;
            track.raycastTarget = true;
            if (onChange != null) slider.onValueChanged.AddListener(v => onChange(v));
            return slider;
        }

        /// <summary>Row of mutually exclusive slanted options (modes, difficulty, language…).</summary>
        public static List<Button> Segmented(Transform parent, string[] options, int selected, Action<int> onSelect, int fontSize = 28)
        {
            var row = Node("Segmented", parent);
            var layout = HStack(row, 10f);
            layout.childForceExpandWidth = true;
            var buttons = new List<Button>();
            for (int i = 0; i < options.Length; i++)
            {
                int index = i;
                Button b = null;
                b = Button(row, options[i], () =>
                {
                    for (int k = 0; k < buttons.Count; k++) StyleSegment(buttons[k], k == index);
                    onSelect(index);
                }, ButtonStyle.Secondary, fontSize);
                b.Layout(-1f, 64f, 1f);
                buttons.Add(b);
            }
            for (int k = 0; k < buttons.Count; k++) StyleSegment(buttons[k], k == selected);
            return buttons;
        }

        /// <summary>Like <see cref="Segmented"/>, wrapped onto rows of <paramref name="columns"/> (languages, many options).</summary>
        public static List<Button> SegmentedGrid(Transform parent, string[] options, int selected, int columns, Action<int> onSelect, int fontSize = 26)
        {
            var grid = Node("SegmentedGrid", parent);
            int rows = (options.Length + columns - 1) / columns;
            const float rowHeight = 64f, gap = 10f;
            grid.Layout(-1f, rows * rowHeight + (rows - 1) * gap);
            var buttons = new List<Button>();
            for (int i = 0; i < options.Length; i++)
            {
                int index = i;
                var b = Button(grid, options[i], () =>
                {
                    for (int k = 0; k < buttons.Count; k++) StyleSegment(buttons[k], k == index);
                    onSelect(index);
                }, ButtonStyle.Secondary, fontSize);
                var rt = b.GetComponent<RectTransform>();
                int row = i / columns, col = i % columns;
                rt.anchorMin = new Vector2(col / (float)columns, 1f);
                rt.anchorMax = new Vector2((col + 1) / (float)columns, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.offsetMin = new Vector2(col == 0 ? 0f : gap * 0.5f, -(row * (rowHeight + gap) + rowHeight));
                rt.offsetMax = new Vector2(col == columns - 1 ? 0f : -gap * 0.5f, -row * (rowHeight + gap));
                buttons.Add(b);
            }
            for (int k = 0; k < buttons.Count; k++) StyleSegment(buttons[k], k == selected);
            return buttons;
        }

        static void StyleSegment(Button b, bool on)
        {
            var body = b.transform.Find("Body").GetComponent<SkewQuad>();
            body.SetColors(on ? UiTheme.Hex("#4FB0FF") : Color.white, on ? UiTheme.Primary : UiTheme.PanelAlt);
            var label = b.GetComponentInChildren<Text>();
            label.color = on ? Color.white : UiTheme.InkSoft;
        }

        public static Toggle Toggle(Transform parent, string label, bool value, Action<bool> onChange)
        {
            var rt = Node("Toggle " + label, parent);
            HStack(rt, 18f, 0, TextAnchor.MiddleLeft);
            var boxRt = Node("Box", rt);
            boxRt.Layout(56f, 56f);
            var box = boxRt.gameObject.AddComponent<Image>();
            box.sprite = UiSprites.RoundedSmall;
            box.type = UnityEngine.UI.Image.Type.Sliced;
            box.color = UiTheme.PanelAlt;
            var check = Label(boxRt, "✓", 40, UiTheme.Primary, TextAnchor.MiddleCenter, FontStyle.Bold);
            check.rectTransform.Fill();
            var text = Label(rt, label, UiTheme.BodySize, UiTheme.Ink);
            text.Layout(-1f, 56f, 1f);
            var toggle = rt.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = box;
            toggle.graphic = check;
            toggle.isOn = value;
            if (onChange != null) toggle.onValueChanged.AddListener(v => onChange(v));
            return toggle;
        }

        /// <summary>Vertical scroll view; returns the content node (VerticalLayoutGroup + ContentSizeFitter).</summary>
        public static ScrollRect ScrollList(Transform parent, out RectTransform content, float spacing = 14f, int padding = 8)
        {
            var rt = Node("Scroll", parent);
            var scroll = rt.gameObject.AddComponent<ScrollRect>();
            var viewport = Node("Viewport", rt).Fill();
            var mask = viewport.gameObject.AddComponent<RectMask2D>();
            mask.padding = Vector4.zero;
            var hit = viewport.gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0);
            content = Node("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            VStack(content, spacing, padding);
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.scrollSensitivity = 30f;
            scroll.decelerationRate = 0.12f;
            return scroll;
        }

        /// <summary>Grid scroll view for card collections.</summary>
        public static ScrollRect ScrollGrid(Transform parent, out RectTransform content, Vector2 cell, Vector2 spacing)
        {
            var scroll = ScrollList(parent, out content, 0f, 0);
            UnityEngine.Object.DestroyImmediate(content.GetComponent<VerticalLayoutGroup>());
            var grid = content.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = cell;
            grid.spacing = spacing;
            grid.padding = new RectOffset(12, 12, 12, 12);
            grid.childAlignment = TextAnchor.UpperCenter;
            return scroll;
        }

        /// <summary>Header bar with slanted title tab and hatch pattern.</summary>
        public static RectTransform Header(Transform parent, string title, Action onBack)
        {
            var bar = Node("Header", parent);
            bar.anchorMin = new Vector2(0f, 1f);
            bar.anchorMax = new Vector2(1f, 1f);
            bar.pivot = new Vector2(0.5f, 1f);
            bar.sizeDelta = new Vector2(0f, 120f);
            bar.anchoredPosition = Vector2.zero;

            if (!UiTheme.Flat)
            {
                var hatch = Image(bar, UiSprites.Hatch, UiTheme.Primary.WithAlpha(0.5f), "Hatch");
                hatch.type = UnityEngine.UI.Image.Type.Tiled;
                hatch.rectTransform.Fill(0f, 0f, 0f, 40f);
            }

            var tab = Skew(bar, UiTheme.Hex("#4FB0FF"), UiTheme.Primary, 40f, "TitleTab");
            tab.rectTransform.At(0f, 1f, onBack != null ? 140f : 30f, -16f, 560f, 88f, 0f, 1f);
            var titleText = FitLabel(tab.rectTransform, title, 46, Color.white, TextAnchor.MiddleLeft);
            titleText.rectTransform.Fill(40f, 4f, 30f, 4f);

            if (onBack != null)
            {
                var back = RoundButton(bar, "‹", onBack, Color.white, UiTheme.PrimaryDark, "Back");
                back.GetComponent<RectTransform>().At(0f, 1f, 30f, -16f, 88f, 88f, 0f, 1f);
            }
            return bar;
        }

        /// <summary>Small rounded pill showing an icon glyph and value (currencies).</summary>
        public static Text Pill(Transform parent, string glyph, Color glyphColor, string value, out RectTransform root)
        {
            root = Node("Pill", parent);
            var bg = Image(root, UiSprites.Rounded, new Color(1f, 1f, 1f, 0.92f), "Bg");
            bg.rectTransform.Fill();
            var icon = Image(root, UiSprites.Circle, glyphColor, "Icon");
            icon.rectTransform.At(0f, 0.5f, 10f, 0f, 48f, 48f, 0f, 0.5f);
            var g = Label(icon.rectTransform, glyph, 28, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            g.rectTransform.Fill();
            var v = Label(root, value, 30, UiTheme.Ink, TextAnchor.MiddleRight, FontStyle.Bold);
            v.rectTransform.Fill(66f, 0f, 22f, 0f);
            return v;
        }

        public static void AddClick(GameObject go, UnityAction action)
        {
            var b = go.GetComponent<Button>() ?? go.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(action);
        }
    }
}

using System;
using System.Collections.Generic;
using FreeVelo.Core.Ui;
using UnityEngine;

namespace FreeVelo.UI
{
    /// <summary>Colours, fonts and text styles of the FreeVelo interface (dark theme, orange accent).</summary>
    public static class Theme
    {
        public static readonly Color Background = Hex(0x0C1015);
        public static readonly Color Surface = Hex(0x161C24);
        public static readonly Color Surface2 = Hex(0x202833);
        public static readonly Color Surface3 = Hex(0x2B3542);
        public static readonly Color Stroke = Hex(0x323D4B);
        public static readonly Color Text = Hex(0xF3F6F9);
        public static readonly Color Muted = Hex(0x97A4B2);
        public static readonly Color Faint = Hex(0x5E6B79);
        public static readonly Color Accent = Hex(0xFF6A2B);
        public static readonly Color AccentDark = Hex(0xD9541C);
        public static readonly Color Success = Hex(0x35C97D);
        public static readonly Color Warning = Hex(0xF5B83D);
        public static readonly Color Danger = Hex(0xFF4D5E);
        public static readonly Color Info = Hex(0x4DA3FF);
        public static readonly Color HeartRed = Hex(0xFF4D6A);

        public const float Radius = 18f;
        public const float RadiusSmall = 12f;

        private static bool _ready;
        private static Font _regular;
        private static Font _medium;
        private static Font _semiBold;
        private static Font _bold;

        public static GUIStyle Display { get; private set; }   // big numbers (power)
        public static GUIStyle Value { get; private set; }     // metric values
        public static GUIStyle H1 { get; private set; }
        public static GUIStyle H2 { get; private set; }
        public static GUIStyle H3 { get; private set; }
        public static GUIStyle Body { get; private set; }
        public static GUIStyle BodyMuted { get; private set; }
        public static GUIStyle Caption { get; private set; }
        public static GUIStyle Overline { get; private set; } // small uppercase labels
        public static GUIStyle ButtonText { get; private set; }
        public static GUIStyle Field { get; private set; }

        public static void Ensure()
        {
            if (_ready) return;
            _ready = true;
            _regular = Resources.Load<Font>("Fonts/Inter-Regular");
            _medium = Resources.Load<Font>("Fonts/Inter-Medium");
            _semiBold = Resources.Load<Font>("Fonts/Inter-SemiBold");
            _bold = Resources.Load<Font>("Fonts/Inter-Bold");

            Display = Style(_bold, 72, Text, TextAnchor.MiddleCenter);
            Value = Style(_bold, 34, Text, TextAnchor.MiddleLeft);
            H1 = Style(_bold, 34, Text, TextAnchor.MiddleLeft);
            H2 = Style(_semiBold, 24, Text, TextAnchor.MiddleLeft);
            H3 = Style(_semiBold, 20, Text, TextAnchor.MiddleLeft);
            Body = Style(_regular, 19, Text, TextAnchor.MiddleLeft, wrap: true);
            BodyMuted = Style(_regular, 17, Muted, TextAnchor.MiddleLeft, wrap: true);
            Caption = Style(_medium, 15, Muted, TextAnchor.MiddleLeft);
            Overline = Style(_semiBold, 13, Muted, TextAnchor.MiddleLeft);
            ButtonText = Style(_semiBold, 19, Text, TextAnchor.MiddleCenter);
            Field = Style(_medium, 21, Text, TextAnchor.MiddleLeft);
            Field.padding = new RectOffset(16, 16, 0, 0);
            Field.clipping = TextClipping.Clip;
            Field.focused.textColor = Text;
            Field.hover.textColor = Text;
            Field.active.textColor = Text;
        }

        private static GUIStyle Style(Font font, int size, Color color, TextAnchor anchor, bool wrap = false)
        {
            var s = new GUIStyle
            {
                font = font,
                fontSize = size,
                alignment = anchor,
                wordWrap = wrap,
                richText = true,
                clipping = TextClipping.Overflow,
            };
            s.normal.textColor = color;
            return s;
        }

        public static GUIStyle With(GUIStyle style, Color? color = null, int? size = null, TextAnchor? anchor = null)
        {
            var s = new GUIStyle(style);
            if (color.HasValue) s.normal.textColor = color.Value;
            if (size.HasValue) s.fontSize = size.Value;
            if (anchor.HasValue) s.alignment = anchor.Value;
            return s;
        }

        public static Color Hex(int rgb, float alpha = 1f) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, alpha);

        public static Color Alpha(Color c, float a) => new Color(c.r, c.g, c.b, c.a * a);
    }

    public enum ButtonKind
    {
        Primary,
        Secondary,
        Ghost,
        Danger,
    }

    /// <summary>Immediate-mode UI components (cards, buttons, chips, controls, scroll areas, toasts) on top of IMGUI.</summary>
    public static class Ui
    {
        private static readonly Dictionary<Icon, Texture2D> IconCache = new Dictionary<Icon, Texture2D>();
        private static readonly Dictionary<int, ScrollState> Scrolls = new Dictionary<int, ScrollState>();
        private static string _toast;
        private static float _toastUntil;

        // ------------------------------------------------------------------ primitives

        /// <summary>Filled rounded rectangle (anti-aliased).</summary>
        public static void Rect(Rect r, Color color, float radius = Theme.Radius)
        {
            if (Event.current.type != EventType.Repaint) return;
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, color, 0, Mathf.Min(radius, Mathf.Min(r.width, r.height) / 2f));
        }

        /// <summary>Rounded outline.</summary>
        public static void Outline(Rect r, Color color, float width, float radius = Theme.Radius)
        {
            if (Event.current.type != EventType.Repaint) return;
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, color, width, Mathf.Min(radius, Mathf.Min(r.width, r.height) / 2f));
        }

        /// <summary>Soft drop shadow under a rounded rectangle.</summary>
        public static void Shadow(Rect r, float radius = Theme.Radius, float strength = 1f)
        {
            if (Event.current.type != EventType.Repaint) return;
            for (int i = 1; i <= 4; i++)
            {
                float g = i * 3f;
                Rect(new Rect(r.x - g + 1, r.y - g + 4, r.width + (2 * g) - 2, r.height + (2 * g)), new Color(0, 0, 0, 0.07f * strength), radius + g);
            }
        }

        /// <summary>Card: shadow and surface.</summary>
        public static void Card(Rect r, Color? color = null, float radius = Theme.Radius)
        {
            Shadow(r, radius);
            Rect(r, color ?? Theme.Surface, radius);
        }

        public static void Label(Rect r, string text, GUIStyle style) => GUI.Label(r, text, style);

        public static void DrawIcon(Rect r, Icon icon, Color color)
        {
            if (Event.current.type != EventType.Repaint) return;
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(r, IconTexture(icon), ScaleMode.ScaleToFit, true);
            GUI.color = old;
        }

        public static Texture2D IconTexture(Icon icon)
        {
            if (IconCache.TryGetValue(icon, out var tex) && tex != null) return tex;
            const int size = 96;
            var alpha = IconPainter.Render(icon, size);
            tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "Icon" + icon, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // IconPainter rows go top-down, Unity textures bottom-up.
                    pixels[((size - 1 - y) * size) + x] = new Color32(255, 255, 255, (byte)(alpha[(y * size) + x] * 255));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(true, true);
            IconCache[icon] = tex;
            return tex;
        }

        public static void Divider(Rect r) => Rect(new Rect(r.x, r.y, r.width, 1), Theme.Stroke, 0);

        // ------------------------------------------------------------------ buttons

        // While a finger/mouse holds an IMGUI button, that button is the hot control (no legacy Input API: the project uses the Input System).
        private static bool Pressed(Rect r) => GUIUtility.hotControl != 0 && r.Contains(Event.current.mousePosition);

        /// <summary>Button with optional icon; returns true when tapped.</summary>
        public static bool Button(Rect r, string text, ButtonKind kind = ButtonKind.Secondary, Icon? icon = null, bool enabled = true)
        {
            bool pressed = enabled && Pressed(r);
            Color bg, fg;
            switch (kind)
            {
                case ButtonKind.Primary:
                    bg = pressed ? Theme.AccentDark : Theme.Accent;
                    fg = Color.white;
                    break;
                case ButtonKind.Danger:
                    bg = Theme.Alpha(Theme.Danger, pressed ? 0.35f : 0.2f);
                    fg = Theme.Danger;
                    break;
                case ButtonKind.Ghost:
                    bg = pressed ? Theme.Surface2 : new Color(0, 0, 0, 0);
                    fg = Theme.Text;
                    break;
                default:
                    bg = pressed ? Theme.Surface3 : Theme.Surface2;
                    fg = Theme.Text;
                    break;
            }

            if (!enabled)
            {
                bg = Theme.Alpha(bg, 0.45f);
                fg = Theme.Alpha(fg, 0.4f);
            }

            float radius = Mathf.Min(Theme.RadiusSmall + 2, r.height / 2f);
            if (bg.a > 0) Rect(r, bg, radius);

            var style = Theme.With(Theme.ButtonText, fg, r.height < 46 ? 16 : (int?)null);
            if (icon.HasValue && string.IsNullOrEmpty(text))
            {
                float s = Mathf.Min(r.height, r.width) * 0.48f;
                DrawIcon(new Rect(r.center.x - (s / 2), r.center.y - (s / 2), s, s), icon.Value, fg);
            }
            else if (icon.HasValue)
            {
                float s = Mathf.Min(r.height * 0.42f, 26);
                var size = style.CalcSize(new GUIContent(text));
                float total = s + 10 + size.x;
                float x = r.center.x - (total / 2f);
                DrawIcon(new Rect(x, r.center.y - (s / 2), s, s), icon.Value, fg);
                GUI.Label(new Rect(x + s + 10, r.y, size.x + 4, r.height), text, Theme.With(style, anchor: TextAnchor.MiddleLeft));
            }
            else
            {
                GUI.Label(r, text, style);
            }

            return GUI.Button(r, GUIContent.none, GUIStyle.none) && enabled;
        }

        /// <summary>Round icon-only button.</summary>
        public static bool IconButton(Rect r, Icon icon, ButtonKind kind = ButtonKind.Secondary, bool enabled = true) => Button(r, null, kind, icon, enabled);

        // ------------------------------------------------------------------ chips, badges, dots

        public static float Chip(Rect r, string text, Color color, Icon? icon = null, bool filled = false)
        {
            var style = Theme.With(Theme.Caption, filled ? Color.white : color, anchor: TextAnchor.MiddleLeft);
            var size = style.CalcSize(new GUIContent(text));
            float iconW = icon.HasValue ? r.height * 0.55f + 6 : 0;
            float w = size.x + iconW + 24;
            var chip = new Rect(r.x, r.y, w, r.height);
            Rect(chip, filled ? color : Theme.Alpha(color, 0.16f), r.height / 2f);
            float x = chip.x + 12;
            if (icon.HasValue)
            {
                float s = r.height * 0.55f;
                DrawIcon(new Rect(x, chip.center.y - (s / 2), s, s), icon.Value, filled ? Color.white : color);
                x += s + 6;
            }

            GUI.Label(new Rect(x, chip.y, size.x + 4, chip.height), text, style);
            return w;
        }

        public static void Dot(Vector2 center, float radius, Color color) => Rect(new Rect(center.x - radius, center.y - radius, radius * 2, radius * 2), color, radius);

        // ------------------------------------------------------------------ controls

        /// <summary>Segmented control; returns the selected index.</summary>
        public static int Segmented(Rect r, string[] options, int selected)
        {
            Rect(r, Theme.Surface2, r.height / 2f);
            float w = (r.width - 8) / options.Length;
            for (int i = 0; i < options.Length; i++)
            {
                var seg = new Rect(r.x + 4 + (i * w), r.y + 4, w, r.height - 8);
                if (i == selected) Rect(seg, Theme.Surface3, seg.height / 2f);
                GUI.Label(seg, options[i], Theme.With(Theme.ButtonText, i == selected ? Theme.Text : Theme.Muted, 17));
                if (GUI.Button(seg, GUIContent.none, GUIStyle.none)) selected = i;
            }

            return selected;
        }

        /// <summary>Value with − / + buttons (and optional larger steps); returns the new value.</summary>
        public static int Stepper(Rect r, int value, string unit, int small, int large, int min, int max)
        {
            float b = r.height;
            bool hasLarge = large > small;
            float x = r.x;
            if (hasLarge && Button(new Rect(x, r.y, b * 1.25f, b), "−" + large)) value -= large;
            if (hasLarge) x += (b * 1.25f) + 6;
            if (Button(new Rect(x, r.y, b, b), null, ButtonKind.Secondary, Icon.ChevronLeft)) value -= small;
            float right = r.xMax;
            if (hasLarge && Button(new Rect(right - (b * 1.25f), r.y, b * 1.25f, b), "+" + large)) value += large;
            if (hasLarge) right -= (b * 1.25f) + 6;
            if (Button(new Rect(right - b, r.y, b, b), null, ButtonKind.Secondary, Icon.ChevronRight)) value += small;
            var mid = new Rect(x + b + 6, r.y, right - b - 6 - (x + b + 6), b);
            GUI.Label(mid, $"{value} <color=#97A4B2><size=16>{unit}</size></color>", Theme.With(Theme.H2, anchor: TextAnchor.MiddleCenter));
            return Mathf.Clamp(value, min, max);
        }

        private static int _sliderDrag;

        /// <summary>Slider with a large touch target; returns the new value (0 … 1 range given by min/max).</summary>
        public static float Slider(Rect r, float value, float min, float max)
        {
            int id = GUIUtility.GetControlID(FocusType.Passive, r);
            var e = Event.current;
            float t = Mathf.InverseLerp(min, max, value);
            var track = new Rect(r.x + 14, r.center.y - 4, r.width - 28, 8);
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown when r.Contains(e.mousePosition):
                    GUIUtility.hotControl = id;
                    _sliderDrag = id;
                    t = Mathf.Clamp01((e.mousePosition.x - track.x) / track.width);
                    e.Use();
                    break;
                case EventType.MouseDrag when GUIUtility.hotControl == id:
                    t = Mathf.Clamp01((e.mousePosition.x - track.x) / track.width);
                    e.Use();
                    break;
                case EventType.MouseUp when GUIUtility.hotControl == id:
                    GUIUtility.hotControl = 0;
                    _sliderDrag = 0;
                    e.Use();
                    break;
            }

            Rect(track, Theme.Surface3, 4);
            Rect(new Rect(track.x, track.y, track.width * t, track.height), Theme.Accent, 4);
            var knob = new Rect(track.x + (track.width * t) - 14, r.center.y - 14, 28, 28);
            Shadow(knob, 14, 0.8f);
            Rect(knob, Color.white, 14);
            return Mathf.Lerp(min, max, t);
        }

        /// <summary>Rounded text field.</summary>
        public static string TextField(Rect r, string text, int maxLength)
        {
            Rect(r, Theme.Surface2, Theme.RadiusSmall);
            if (GUIUtility.keyboardControl != 0) Outline(r, Theme.Alpha(Theme.Accent, 0.6f), 2, Theme.RadiusSmall);
            return GUI.TextField(r, text ?? string.Empty, maxLength, Theme.Field);
        }

        // ------------------------------------------------------------------ scrolling (drag with the finger)

        private sealed class ScrollState
        {
            public float Offset;
            public float Velocity;
            public bool Pending;
            public bool Dragging;
            public float StartY;
            public float StartOffset;
            public float LastY;
        }

        /// <summary>
        /// Begins a vertically scrollable area. Content is laid out in local coordinates (0,0 = top left of the
        /// view); dragging with the finger scrolls without triggering the buttons underneath.
        /// </summary>
        public static void BeginScroll(int key, Rect view, float contentHeight)
        {
            if (!Scrolls.TryGetValue(key, out var s)) Scrolls[key] = s = new ScrollState();
            float max = Mathf.Max(0, contentHeight - view.height);
            var e = Event.current;
            switch (e.type)
            {
                case EventType.MouseDown when view.Contains(e.mousePosition):
                    s.Pending = true;
                    s.StartY = e.mousePosition.y;
                    s.LastY = s.StartY;
                    s.StartOffset = s.Offset;
                    s.Velocity = 0;
                    break;
                case EventType.MouseDrag when s.Pending || s.Dragging:
                    if (!s.Dragging && Mathf.Abs(e.mousePosition.y - s.StartY) > 12)
                    {
                        s.Dragging = true;
                        GUIUtility.hotControl = 0; // cancel the button press underneath
                    }

                    if (s.Dragging)
                    {
                        s.Velocity = s.LastY - e.mousePosition.y;
                        s.LastY = e.mousePosition.y;
                        s.Offset = Mathf.Clamp(s.StartOffset + (s.StartY - e.mousePosition.y), 0, max);
                        e.Use();
                    }

                    break;
                case EventType.MouseUp when s.Pending || s.Dragging:
                    bool wasDragging = s.Dragging;
                    s.Pending = false;
                    s.Dragging = false;
                    if (wasDragging) e.Use();
                    break;
                case EventType.ScrollWheel when view.Contains(e.mousePosition):
                    s.Offset = Mathf.Clamp(s.Offset + (e.delta.y * 30), 0, max);
                    e.Use();
                    break;
                case EventType.Repaint when !s.Dragging && Mathf.Abs(s.Velocity) > 0.5f:
                    s.Offset = Mathf.Clamp(s.Offset + s.Velocity, 0, max); // fling
                    s.Velocity *= 0.92f;
                    break;
            }

            s.Offset = Mathf.Clamp(s.Offset, 0, max);
            GUI.BeginGroup(view);
            GUI.BeginGroup(new Rect(0, -s.Offset, view.width, Mathf.Max(contentHeight, view.height)));
        }

        public static void EndScroll(int key, Rect view, float contentHeight)
        {
            GUI.EndGroup();
            GUI.EndGroup();

            // Scroll indicator
            if (contentHeight > view.height && Scrolls.TryGetValue(key, out var s))
            {
                float h = Mathf.Max(40, view.height * view.height / contentHeight);
                float y = view.y + ((view.height - h) * (s.Offset / (contentHeight - view.height)));
                Rect(new Rect(view.xMax - 5, y, 4, h), Theme.Alpha(Theme.Muted, 0.5f), 2);
            }
        }

        // ------------------------------------------------------------------ toast

        public static void Toast(string text, float seconds = 4f)
        {
            _toast = text;
            _toastUntil = Time.realtimeSinceStartup + seconds;
        }

        public static void DrawToast(float screenW, float screenH)
        {
            if (string.IsNullOrEmpty(_toast) || Time.realtimeSinceStartup > _toastUntil) return;
            float a = Mathf.Clamp01((_toastUntil - Time.realtimeSinceStartup) / 0.4f);
            var style = Theme.With(Theme.Body, anchor: TextAnchor.MiddleCenter);
            style.wordWrap = true;
            float w = Mathf.Min(760, screenW - 80);
            float h = Mathf.Max(56, style.CalcHeight(new GUIContent(_toast), w - 40) + 24);
            var r = new Rect((screenW - w) / 2f, screenH - h - 28, w, h);
            var old = GUI.color;
            GUI.color = new Color(1, 1, 1, a);
            Card(r, Theme.Surface3, h / 2f > 30 ? Theme.Radius : h / 2f);
            GUI.Label(new Rect(r.x + 20, r.y, r.width - 40, r.height), _toast, style);
            GUI.color = old;
        }

        // ------------------------------------------------------------------ helpers

        public static string Clock(double seconds)
        {
            int s = Mathf.Max(0, Mathf.FloorToInt((float)seconds));
            return s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}";
        }

        public static string Minutes(int seconds) => seconds >= 3600 ? $"{seconds / 3600}h {seconds / 60 % 60:00}m" : $"{Math.Max(1, seconds / 60)} min";
    }
}

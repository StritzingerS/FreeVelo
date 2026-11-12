using System.Collections.Generic;
using UnityEngine;

namespace FreeVelo.UI
{
    /// <summary>
    /// Finds overlapping UI while the app runs, in the editor and in development builds only: every card and button
    /// drawn in a frame is recorded, and afterwards any two that collide are outlined in red and logged once
    /// ("UI overlap: …"). The UI is laid out by hand in absolute rectangles, so this is the safety net for cards that
    /// do not know about each other, texts that grew, or a screen size nobody tried.
    /// Rules: two cards may not intersect unless one contains the other; two buttons may not intersect; a button may
    /// not stick out of a card it touches. Things inside scroll views are skipped (they are clipped), and anything
    /// drawn after <see cref="Modal"/> is on a new layer (dialogs lie on top of the screen on purpose).
    /// </summary>
    public static class UiCheck
    {
        public enum Kind
        {
            Card,
            Button,
        }

        private struct Region
        {
            public Rect Rect;
            public Kind Kind;
            public int Layer;
        }

        private static readonly List<Region> Regions = new List<Region>();
        private static readonly HashSet<string> Reported = new HashSet<string>();
        private static readonly List<Rect> Marks = new List<Rect>();
        private static int _layer;
        private static int _scrollDepth;
        private static string _screen = "";

        /// <summary>Overlaps found so far (for tooling), newest last.</summary>
        public static readonly List<string> Problems = new List<string>();

        public static bool Active => Application.isEditor || Debug.isDebugBuild;

        /// <summary>Start of a UI pass; <paramref name="screen"/> names what is shown (for the log).</summary>
        public static void Begin(string screen)
        {
            if (!Active || Event.current.type != EventType.Repaint) return;
            Regions.Clear();
            _layer = 0;
            _scrollDepth = 0;
            _screen = screen;
        }

        /// <summary>What is drawn from here on lies on top of the rest on purpose (a dialog, a toast).</summary>
        public static void Modal()
        {
            if (Active && Event.current.type == EventType.Repaint) _layer++;
        }

        public static void EnterScroll()
        {
            if (Active && Event.current.type == EventType.Repaint) _scrollDepth++;
        }

        public static void LeaveScroll()
        {
            if (Active && Event.current.type == EventType.Repaint) _scrollDepth = Mathf.Max(0, _scrollDepth - 1);
        }

        public static void Track(Rect r, Kind kind)
        {
            if (!Active || Event.current.type != EventType.Repaint || _scrollDepth > 0 || r.width <= 0 || r.height <= 0) return;
            // Screen pixels, so groups and the UI scaling do not matter.
            Regions.Add(new Region { Rect = GUIUtility.GUIToScreenRect(r), Kind = kind, Layer = _layer });
        }

        /// <summary>End of the pass: checks, logs new problems and outlines the colliding rectangles.</summary>
        public static void End()
        {
            if (!Active || Event.current.type != EventType.Repaint) return;
            Marks.Clear();
            const float tolerance = 3f; // pixels: shadows and rounding may touch
            for (int i = 0; i < Regions.Count; i++)
            {
                for (int j = i + 1; j < Regions.Count; j++)
                {
                    var a = Regions[i];
                    var b = Regions[j];
                    if (a.Layer != b.Layer) continue;
                    float w = Mathf.Min(a.Rect.xMax, b.Rect.xMax) - Mathf.Max(a.Rect.xMin, b.Rect.xMin);
                    float h = Mathf.Min(a.Rect.yMax, b.Rect.yMax) - Mathf.Max(a.Rect.yMin, b.Rect.yMin);
                    if (w <= tolerance || h <= tolerance) continue;
                    bool aInB = Contains(b.Rect, a.Rect, tolerance), bInA = Contains(a.Rect, b.Rect, tolerance);
                    bool bad = a.Kind == Kind.Button && b.Kind == Kind.Button
                        || (a.Kind == Kind.Card && b.Kind == Kind.Card && !aInB && !bInA)
                        || (a.Kind == Kind.Button && b.Kind == Kind.Card && !aInB)
                        || (a.Kind == Kind.Card && b.Kind == Kind.Button && !bInA);
                    if (!bad) continue;
                    Marks.Add(a.Rect);
                    Marks.Add(b.Rect);
                    string what = $"UI overlap on {_screen}: {a.Kind} {Describe(a.Rect)} and {b.Kind} {Describe(b.Rect)} share {w:0}×{h:0} px (screen {Screen.width}×{Screen.height})";
                    string key = $"{_screen}|{a.Kind}|{b.Kind}|{Mathf.RoundToInt(a.Rect.x / 8)}|{Mathf.RoundToInt(a.Rect.y / 8)}|{Mathf.RoundToInt(b.Rect.x / 8)}|{Mathf.RoundToInt(b.Rect.y / 8)}";
                    if (Reported.Add(key))
                    {
                        Problems.Add(what);
                        Debug.LogWarning(what);
                    }
                }
            }

            if (Marks.Count == 0) return;
            var matrix = GUI.matrix;
            GUI.matrix = Matrix4x4.identity; // the marks are in screen pixels
            foreach (var m in Marks) Ui.Outline(m, new Color(1f, 0.1f, 0.1f, 0.9f), 3, 6);
            GUI.matrix = matrix;
        }

        private static bool Contains(Rect outer, Rect inner, float tolerance) =>
            inner.xMin >= outer.xMin - tolerance && inner.yMin >= outer.yMin - tolerance && inner.xMax <= outer.xMax + tolerance && inner.yMax <= outer.yMax + tolerance;

        private static string Describe(Rect r) => $"({r.x:0},{r.y:0} {r.width:0}×{r.height:0})";
    }
}

using System.Collections.Generic;
using System.Linq;
using FreeVelo.App;
using FreeVelo.Core.Ui;
using FreeVelo.Core.Workouts;
using UnityEngine;

namespace FreeVelo.UI
{
    /// <summary>
    /// Workout editor screen: zone palette (tap to add after the selection, or drag onto the timeline), timeline
    /// with drag &amp; drop (drag a block to move it, its top edge to change power, its right edge to change
    /// duration), panel with fine adjustment for the selected block, undo/redo, save.
    /// </summary>
    public sealed partial class Hud
    {
        private enum Gesture
        {
            None,
            PendingMove,
            Move,
            Power,
            PowerRampEnd,
            Duration,
            PendingPalette,
            Palette,
        }

        private WorkoutEditor _editor;
        private bool _editingBuiltIn;
        private string _editorName;
        private string _editorMessage;
        private bool _confirmDiscard;
        private Gesture _gesture;
        private int _gestureIndex;
        private Vector2 _gestureStart;
        private double _gestureValue;
        private float _gestureSecondsPerPx;
        private int _paletteItem;
        private int _dropIndex = -1;
        private readonly List<(int Index, Rect Rect)> _blockRects = new List<(int, Rect)>();

        // Zone palette: label key, % FTP (middle of the Coggan zone).
        private static readonly (string Label, double Percent)[] Palette =
        {
            ("Z1", 50), ("Z2", 68), ("Z3", 83), ("Z4", 98), ("Z5", 113), ("Z6", 135),
        };

        private const int RampItem = 6;
        private const int FreeItem = 7;
        private const int IntervalItem = 8;

        private static GUIStyle _tinyCenterStyle;

        private static GUIStyle TinyCenter => _tinyCenterStyle ??= Theme.With(Theme.Caption, Theme.Text, 14, TextAnchor.MiddleCenter);

        private void OpenEditor(Workout workout, bool builtIn)
        {
            _editor = workout != null
                ? new WorkoutEditor(workout, AppSettings.FtpW)
                : WorkoutEditor.CreateNew(L.Get("new_workout"), AppSettings.FtpW);
            _editingBuiltIn = builtIn;
            _editorName = _editor.Workout.Name.Get(AppSettings.Language);
            _editorMessage = null;
            _confirmDiscard = false;
            _gesture = Gesture.None;
            _editor.Selected = _editor.Blocks.Count > 0 ? 0 : -1;
            _page = Page.Editor;
        }

        private void CloseEditor()
        {
            _editor = null;
            _page = Page.Workouts;
        }

        private void DrawEditor()
        {
            var ed = _editor;
            ed.FtpW = AppSettings.FtpW;
            Ui.Rect(_full, Theme.Alpha(Theme.Background, 0.94f), 0);
            var area = new Rect(28, 20, _w - 56, _h - 40);

            // ---- top bar: close, name, undo/redo, unit, save
            float x = area.x, y = area.y;
            if (Ui.IconButton(new Rect(x, y, 52, 52), _confirmDiscard ? Icon.Trash : Icon.ChevronLeft, _confirmDiscard ? ButtonKind.Danger : ButtonKind.Secondary))
            {
                if (!ed.IsDirty || _confirmDiscard)
                {
                    CloseEditor();
                    return;
                }

                _confirmDiscard = true;
                Ui.Toast(L.Get("discard_hint"));
            }

            string name = Ui.TextField(new Rect(x + 64, y, Mathf.Clamp(area.width - 760, 240, 460), 52), _editorName, 60);
            if (name != _editorName)
            {
                _editorName = name;
                ed.Rename(name);
            }

            float bx = area.xMax;
            float sw = Ui.ButtonWidth(L.Get("save"), true);
            bx -= sw;
            if (Ui.Button(new Rect(bx, y, sw, 52), L.Get("save"), ButtonKind.Primary, Icon.Check)) SaveEditor(asCopy: false);
            if (_editingBuiltIn)
            {
                float cw = Ui.ButtonWidth(L.Get("save_copy"), true);
                bx -= cw + 10;
                if (Ui.Button(new Rect(bx, y, cw, 52), L.Get("save_copy"), ButtonKind.Secondary, Icon.Copy)) SaveEditor(asCopy: true);
            }

            bool watts = UsesWatts(ed.Workout);
            bx -= 140;
            int unit = Ui.Segmented(new Rect(bx, y, 130, 52), new[] { "%", "W" }, watts ? 1 : 0);
            if ((unit == 1) != watts) ed.SetUnit(unit == 1);
            bx -= 62;
            if (Ui.IconButton(new Rect(bx, y, 52, 52), Icon.Redo, ButtonKind.Secondary, ed.CanRedo)) ed.Redo();
            bx -= 60;
            if (Ui.IconButton(new Rect(bx, y, 52, 52), Icon.Undo, ButtonKind.Secondary, ed.CanUndo)) ed.Undo();

            // ---- stats chips
            y += 66;
            float cx = x;
            cx += Ui.Chip(new Rect(cx, y, 0, 32), Ui.Clock(ed.Workout.TotalDurationS), Theme.Info, Icon.Timer) + 8;
            cx += Ui.Chip(new Rect(cx, y, 0, 32), $"TSS {ed.EstimatedTss():0}", Theme.Accent, Icon.Bolt) + 8;
            cx += Ui.Chip(new Rect(cx, y, 0, 32), $"FTP {ed.FtpW} W", Theme.Muted) + 8;
            if (ed.IsDirty) Ui.Chip(new Rect(cx, y, 0, 32), L.Get("unsaved"), Theme.Warning, Icon.Edit);
            if (!string.IsNullOrEmpty(_editorMessage))
            {
                Ui.Toast(_editorMessage);
                _editorMessage = null;
            }

            // ---- palette
            y += 46;
            var paletteRects = new List<Rect>();
            int items = ed.OpenRepeat != null ? 8 : 9;
            float pw = Mathf.Min(124, (area.width - (10 * (items - 1))) / items);
            for (int i = 0; i < items; i++)
            {
                var r = new Rect(x + (i * (pw + 10)), y, pw, 60);
                paletteRects.Add(r);
                DrawPaletteItem(r, i);
            }

            // ---- breadcrumb inside a repeat block / hint
            y += 72;
            if (ed.OpenRepeat != null)
            {
                Ui.Chip(new Rect(x, y, 0, 34), $"{L.Get("intervals")} ×{ed.OpenRepeat.Count} — {L.Get("editing_intervals")}", Theme.Accent, Icon.Reset);
                if (Ui.Button(new Rect(area.xMax - 160, y - 6, 160, 44), L.Get("done"), ButtonKind.Primary, Icon.Check)) ed.CloseRepeat();
            }
            else
            {
                GUI.Label(new Rect(x, y, area.width, 30), L.Get("editor_hint"), Theme.Caption);
            }

            // ---- timeline
            y += 44;
            var timeline = new Rect(x, y, area.width, 200);
            Ui.Card(timeline);
            DrawTimeline(timeline);
            HandleEditorInput(timeline, paletteRects);

            // ---- selected block panel
            DrawBlockPanel(new Rect(x, timeline.yMax + 12, area.width, area.yMax - timeline.yMax - 12));
        }

        private void SaveEditor(bool asCopy)
        {
            var library = WorkoutStore.Library;
            var workout = _editor.Workout;
            if (workout.Blocks.Count == 0)
            {
                _editorMessage = "✗ " + L.Get("empty_workout");
                return;
            }

            if (asCopy || string.IsNullOrEmpty(workout.Id))
            {
                if (asCopy) workout.Name = new LocalizedText($"{workout.Name.Get(AppSettings.Language)} ({L.Get("copy")})");
                workout.Id = library.UniqueId(workout.Name.Get("en"));
                workout.Category = WorkoutCategory.Custom;
                _editorName = workout.Name.Get(AppSettings.Language);
                _editingBuiltIn = false;
            }

            library.Save(WorkoutFiles.Clone(workout));
            _editor.MarkSaved();
            _confirmDiscard = false;
            _editorMessage = "✓ " + L.Get("saved");
        }

        private static bool UsesWatts(Workout w) => Targets(w.Blocks).Any(t => t.Unit == TargetUnit.Watts);

        private static IEnumerable<PowerTarget> Targets(IEnumerable<WorkoutBlock> blocks)
        {
            foreach (var b in blocks)
            {
                switch (b)
                {
                    case SteadyBlock s: yield return s.Target; break;
                    case RampBlock r:
                        yield return r.From;
                        yield return r.To;
                        break;
                    case RepeatBlock rep:
                        foreach (var t in Targets(rep.Blocks)) yield return t;
                        break;
                }
            }
        }

        private void DrawPaletteItem(Rect r, int item)
        {
            bool dragging = _gesture == Gesture.Palette && _paletteItem == item;
            Color color;
            string title, sub;
            Icon? icon = null;
            if (item < Palette.Length)
            {
                color = ZoneColor(Palette[item].Percent / 100.0 * AppSettings.FtpW, AppSettings.FtpW);
                title = Palette[item].Label;
                sub = $"{Palette[item].Percent:0} %";
            }
            else
            {
                color = Theme.Surface3;
                title = item == RampItem ? L.Get("ramp") : item == FreeItem ? L.Get("free") : L.Get("intervals");
                sub = null;
                icon = item == RampItem ? Icon.Route : item == FreeItem ? Icon.Bike : Icon.Reset;
            }

            Ui.Rect(r, Theme.Alpha(color, dragging ? 0.4f : 1f), Theme.RadiusSmall);
            bool dark = item < Palette.Length;
            var textColor = dark ? Theme.Background : Theme.Text;
            if (icon.HasValue)
            {
                Ui.DrawIcon(new Rect(r.center.x - 11, r.y + 8, 22, 22), icon.Value, textColor);
                GUI.Label(new Rect(r.x, r.y + 32, r.width, 22), title, Theme.With(Theme.Caption, textColor, 14, TextAnchor.MiddleCenter));
            }
            else
            {
                GUI.Label(new Rect(r.x, r.y + 6, r.width, 26), title, Theme.With(Theme.H3, textColor, 19, TextAnchor.MiddleCenter));
                GUI.Label(new Rect(r.x, r.y + 32, r.width, 20), sub, Theme.With(Theme.Caption, Theme.Alpha(textColor, 0.75f), 14, TextAnchor.MiddleCenter));
            }
        }

        // ------------------------------------------------------------------ timeline

        private float SecondsPerPx(Rect timeline)
        {
            if (_gesture == Gesture.Duration || _gesture == Gesture.Power || _gesture == Gesture.PowerRampEnd) return _gestureSecondsPerPx;
            int total = Mathf.Max(60, _editor.Blocks.Sum(BlockSeconds));
            return total / (timeline.width - 20);
        }

        /// <summary>Width of a block on the timeline: repeat blocks show their children once.</summary>
        private static int BlockSeconds(WorkoutBlock b) => b is RepeatBlock r ? r.Blocks.Sum(c => c.TotalDurationS) : b.TotalDurationS;

        private float PercentToY(Rect t, double percent) => t.yMax - 8 - (Mathf.Clamp((float)percent, 0, 170) / 170f * (t.height - 36));

        private void DrawTimeline(Rect t)
        {
            var ed = _editor;
            int ftp = ed.FtpW;
            float spp = SecondsPerPx(t);
            float x = t.x + 10;
            _blockRects.Clear();

            // 100 % FTP line
            var old = GUI.color;
            GUI.color = Theme.Out(new Color(1, 1, 1, 0.25f));
            float ftpY = PercentToY(t, 100);
            GUI.DrawTexture(new Rect(t.x + 4, ftpY, t.width - 8, 1), Texture2D.whiteTexture);
            GUI.color = old;
            GUI.Label(new Rect(t.xMax - 60, ftpY - 20, 50, 18), "FTP", Theme.With(Theme.Caption, size: 13));

            for (int i = 0; i < ed.Blocks.Count; i++)
            {
                var block = ed.Blocks[i];
                float width = Mathf.Max(14, BlockSeconds(block) / spp);
                var rect = new Rect(x, t.y + 6, width, t.height - 12);
                _blockRects.Add((i, rect));
                bool dragged = _gesture == Gesture.Move && _gestureIndex == i;
                GUI.color = dragged ? new Color(1, 1, 1, 0.35f) : Color.white;
                DrawBlock(t, rect, block, ftp);
                GUI.color = Color.white;

                if (i == ed.Selected)
                {
                    Ui.Outline(new Rect(rect.x - 2, t.y + 4, rect.width + 4, t.height - 8), Theme.Text, 3, 8);
                    DrawHandles(t, rect, block);
                }

                x += width;
            }

            if (_dropIndex >= 0)
            {
                float dropX = _dropIndex < _blockRects.Count ? _blockRects[_dropIndex].Rect.x : x;
                Ui.Rect(new Rect(dropX - 3, t.y + 4, 6, t.height - 8), Theme.Accent, 3);
                GUI.color = old;
            }
        }

        private void DrawBlock(Rect t, Rect rect, WorkoutBlock block, int ftp)
        {
            var tint = GUI.color;
            switch (block)
            {
                case SteadyBlock s:
                    Bar(rect.x, rect.width, s.Target.ToPercent(ftp), s.Target.ToPercent(ftp));
                    break;
                case RampBlock r:
                    Bar(rect.x, rect.width, r.From.ToPercent(ftp), r.To.ToPercent(ftp));
                    break;
                case FreeRideBlock f:
                {
                    float top = PercentToY(t, 75);
                    GUI.color = Theme.Out(new Color(0.55f, 0.58f, 0.62f, 0.75f * tint.a));
                    GUI.DrawTexture(new Rect(rect.x + 1, top, rect.width - 2, t.yMax - 8 - top), Texture2D.whiteTexture);
                    GUI.color = tint;
                    GUI.Label(new Rect(rect.x, top - 26, rect.width, 24), f.IsMaxEffort ? "MAX" : L.Get("free"), TinyCenter);
                    break;
                }

                case RepeatBlock rep:
                {
                    float cx = rect.x;
                    float scale = rect.width / Mathf.Max(1, rep.Blocks.Sum(c => c.TotalDurationS));
                    foreach (var child in rep.Blocks)
                    {
                        float w = child.TotalDurationS * scale;
                        switch (child)
                        {
                            case SteadyBlock s: Bar(cx, w, s.Target.ToPercent(ftp), s.Target.ToPercent(ftp)); break;
                            case RampBlock r: Bar(cx, w, r.From.ToPercent(ftp), r.To.ToPercent(ftp)); break;
                            default:
                                GUI.color = Theme.Out(new Color(0.55f, 0.58f, 0.62f, 0.75f * tint.a));
                                GUI.DrawTexture(new Rect(cx, PercentToY(t, 75), w - 1, t.yMax - 8 - PercentToY(t, 75)), Texture2D.whiteTexture);
                                break;
                        }

                        cx += w;
                    }

                    GUI.color = Theme.Out(new Color(1, 1, 1, 0.8f * tint.a));
                    GUI.DrawTexture(new Rect(rect.x + 2, t.y + 8, rect.width - 4, 3), Texture2D.whiteTexture);
                    GUI.color = tint;
                    GUI.Label(new Rect(rect.x, t.y + 12, rect.width, 26), $"×{rep.Count}", TinyCenter);
                    break;
                }
            }

            GUI.color = tint;
            if (rect.width > 46) GUI.Label(new Rect(rect.x, t.yMax - 30, rect.width, 24), Ui.Clock(block.TotalDurationS), TinyCenter);

            void Bar(float bx, float bw, double fromPct, double toPct)
            {
                int steps = Mathf.Abs((float)(toPct - fromPct)) < 0.5 ? 1 : Mathf.Max(1, Mathf.CeilToInt(bw / 5));
                for (int i = 0; i < steps; i++)
                {
                    double pct = fromPct + ((toPct - fromPct) * (i + 0.5) / steps);
                    float top = PercentToY(t, pct);
                    var c = ZoneColor(pct / 100.0 * ftp, ftp);
                    GUI.color = Theme.Out(new Color(c.r, c.g, c.b, tint.a));
                    GUI.DrawTexture(new Rect(bx + (i * bw / steps), top, Mathf.Max(1, (bw / steps) - (steps == 1 ? 1 : 0)), t.yMax - 8 - top), Texture2D.whiteTexture);
                }

                GUI.color = tint;
                if (bw > 40) GUI.Label(new Rect(bx, PercentToY(t, Mathf.Max((float)fromPct, (float)toPct)) - 24, bw, 22), PowerText(fromPct, toPct), TinyCenter);
            }
        }

        private string PowerText(double fromPct, double toPct)
        {
            bool watts = UsesWatts(_editor.Workout);
            string One(double p) => watts ? $"{p / 100.0 * _editor.FtpW:0}W" : $"{p:0}%";
            return Mathf.Abs((float)(toPct - fromPct)) < 0.5 ? One(fromPct) : $"{One(fromPct)}→{One(toPct)}";
        }

        private void DrawHandles(Rect t, Rect rect, WorkoutBlock block)
        {
            foreach (var h in Handles(t, rect, block)) DrawKnob(h.Rect, Theme.Text);
        }

        private IEnumerable<(Gesture Kind, Rect Rect)> Handles(Rect t, Rect rect, WorkoutBlock block)
        {
            int ftp = _editor.FtpW;
            if (!(block is RepeatBlock)) yield return (Gesture.Duration, new Rect(rect.xMax - 14, t.center.y - 22, 28, 44));
            switch (block)
            {
                case SteadyBlock s:
                    yield return (Gesture.Power, new Rect(rect.center.x - 22, PercentToY(t, s.Target.ToPercent(ftp)) - 14, 44, 28));
                    break;
                case RampBlock r:
                    yield return (Gesture.Power, new Rect(rect.x + 2, PercentToY(t, r.From.ToPercent(ftp)) - 14, 40, 28));
                    yield return (Gesture.PowerRampEnd, new Rect(rect.xMax - 42, PercentToY(t, r.To.ToPercent(ftp)) - 14, 40, 28));
                    break;
            }
        }

        private static void DrawKnob(Rect r, Color c)
        {
            var knob = new Rect(r.center.x - 12, r.center.y - 12, 24, 24);
            Ui.Shadow(knob, 12, 0.8f);
            Ui.Rect(knob, c, 12);
            Ui.Rect(new Rect(knob.center.x - 5, knob.center.y - 5, 10, 10), Theme.Accent, 5);
        }

        private static void DrawOutline(Rect r, Color c, float thickness)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.yMax - thickness, r.width, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.y, thickness, r.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.xMax - thickness, r.y, thickness, r.height), Texture2D.whiteTexture);
            GUI.color = old;
        }

        // ------------------------------------------------------------------ input (drag & drop)

        private void HandleEditorInput(Rect timeline, List<Rect> palette)
        {
            var ed = _editor;
            var e = Event.current;
            var p = e.mousePosition;

            switch (e.type)
            {
                case EventType.MouseDown:
                {
                    _confirmDiscard = false;

                    // Palette: tap adds, drag drops onto the timeline.
                    for (int i = 0; i < palette.Count; i++)
                    {
                        if (!palette[i].Contains(p)) continue;
                        _gesture = Gesture.PendingPalette;
                        _paletteItem = i;
                        _gestureStart = p;
                        e.Use();
                        return;
                    }

                    if (!timeline.Contains(p)) return;

                    // Handles of the selected block first (they are larger than the edge itself).
                    if (ed.SelectedBlock != null)
                    {
                        var sel = _blockRects.FirstOrDefault(b => b.Index == ed.Selected);
                        foreach (var h in Handles(timeline, sel.Rect, ed.SelectedBlock))
                        {
                            if (!h.Rect.Contains(p)) continue;
                            StartEdgeGesture(h.Kind, timeline, p);
                            e.Use();
                            return;
                        }
                    }

                    foreach (var (index, rect) in _blockRects)
                    {
                        if (!rect.Contains(p)) continue;
                        ed.Selected = index;
                        _gesture = Gesture.PendingMove;
                        _gestureIndex = index;
                        _gestureStart = p;
                        e.Use();
                        return;
                    }

                    ed.Selected = -1;
                    e.Use();
                    break;
                }

                case EventType.MouseDrag:
                    if (_gesture == Gesture.None) return;
                    UpdateGesture(timeline, p);
                    e.Use();
                    break;

                case EventType.MouseUp:
                    if (_gesture == Gesture.None) return;
                    FinishGesture(timeline, p);
                    e.Use();
                    break;
            }
        }

        private void StartEdgeGesture(Gesture kind, Rect timeline, Vector2 p)
        {
            var ed = _editor;
            _gesture = kind;
            _gestureIndex = ed.Selected;
            _gestureStart = p;
            _gestureSecondsPerPx = SecondsPerPx(timeline);
            switch (ed.SelectedBlock)
            {
                case TimedBlock tb when kind == Gesture.Duration: _gestureValue = tb.DurationS; break;
                case SteadyBlock s: _gestureValue = s.Target.ToPercent(ed.FtpW); break;
                case RampBlock r: _gestureValue = (kind == Gesture.PowerRampEnd ? r.To : r.From).ToPercent(ed.FtpW); break;
            }

            ed.BeginGesture();
        }

        private void UpdateGesture(Rect timeline, Vector2 p)
        {
            var ed = _editor;
            var delta = p - _gestureStart;
            switch (_gesture)
            {
                case Gesture.PendingMove:
                    if (Mathf.Abs(delta.x) > 14) _gesture = Gesture.Move;
                    break;
                case Gesture.Move:
                    _dropIndex = DropIndex(p.x, timeline);
                    break;
                case Gesture.PendingPalette:
                    if (delta.magnitude > 14) _gesture = Gesture.Palette;
                    break;
                case Gesture.Palette:
                    _dropIndex = timeline.Contains(p) ? DropIndex(p.x, timeline) : -1;
                    break;
                case Gesture.Duration:
                    ed.SetDuration(_gestureIndex, SnapSeconds(_gestureValue + (delta.x * _gestureSecondsPerPx)));
                    break;
                case Gesture.Power:
                case Gesture.PowerRampEnd:
                {
                    float pxPerPercent = (timeline.height - 36) / 170f;
                    double pct = Mathf.Round((float)(_gestureValue - (delta.y / pxPerPercent)));
                    ed.SetPercent(_gestureIndex, pct, _gesture == Gesture.PowerRampEnd);
                    break;
                }
            }
        }

        private void FinishGesture(Rect timeline, Vector2 p)
        {
            var ed = _editor;
            switch (_gesture)
            {
                case Gesture.Move:
                {
                    int to = DropIndex(p.x, timeline);
                    if (to > _gestureIndex) to--; // the block itself leaves its old place
                    ed.Move(_gestureIndex, to);
                    break;
                }

                case Gesture.PendingPalette:
                    AddPaletteItem(_paletteItem, null);
                    break;
                case Gesture.Palette:
                    if (timeline.Contains(p)) AddPaletteItem(_paletteItem, DropIndex(p.x, timeline));
                    break;
                case Gesture.Duration:
                case Gesture.Power:
                case Gesture.PowerRampEnd:
                    ed.EndGesture();
                    break;
            }

            _gesture = Gesture.None;
            _dropIndex = -1;
        }

        /// <summary>Insertion position (0 … count) for a drop at screen x.</summary>
        private int DropIndex(float x, Rect timeline)
        {
            for (int i = 0; i < _blockRects.Count; i++)
            {
                if (x < _blockRects[i].Rect.center.x) return i;
            }

            return _blockRects.Count;
        }

        private void AddPaletteItem(int item, int? index)
        {
            var ed = _editor;
            if (item < Palette.Length) ed.AddSteady(Palette[item].Percent, 300, index);
            else if (item == RampItem) ed.AddRamp(50, 75, 600, index);
            else if (item == FreeItem) ed.AddFreeRide(300, index);
            else if (item == IntervalItem && ed.OpenRepeat == null) ed.AddIntervals(4, 180, 113, 180, 55, index);
        }

        private static int SnapSeconds(double seconds)
        {
            int step = seconds < 300 ? 15 : seconds < 1200 ? 30 : 60;
            return Mathf.Max(WorkoutEditor.MinDurationS, Mathf.RoundToInt((float)(seconds / step)) * step);
        }

        // ------------------------------------------------------------------ selected block panel

        private void DrawBlockPanel(Rect r)
        {
            var ed = _editor;
            var block = ed.SelectedBlock;
            Ui.Card(r);
            if (block == null)
            {
                Ui.DrawIcon(new Rect(r.x + 24, r.center.y - 14, 28, 28), Icon.Edit, Theme.Muted);
                GUI.Label(new Rect(r.x + 64, r.y, r.width - 80, r.height), L.Get("select_block"), Theme.BodyMuted);
                return;
            }

            int i = ed.Selected;
            int ftp = ed.FtpW;
            string type = block is SteadyBlock ? L.Get("steady") : block is RampBlock ? L.Get("ramp") : block is FreeRideBlock ? L.Get("free") : L.Get("intervals");
            GUI.Label(new Rect(r.x + 24, r.y + 12, 300, 20), type.ToUpperInvariant(), Theme.Overline);

            float rowY = r.y + 36;
            float rowW = Mathf.Min(560, r.width - 420);
            switch (block)
            {
                case SteadyBlock s:
                    DurationRow(new Rect(r.x + 24, rowY, rowW, 46), i, s.DurationS);
                    PowerRow(new Rect(r.x + 24, rowY + 54, rowW, 46), L.Get("power"), s.Target.ToPercent(ftp), v => ed.SetPercent(i, v));
                    break;
                case RampBlock rb:
                    DurationRow(new Rect(r.x + 24, rowY, rowW, 46), i, rb.DurationS);
                    float half = (rowW - 12) / 2f;
                    PowerRow(new Rect(r.x + 24, rowY + 54, rowW, 46), L.Get("from"), rb.From.ToPercent(ftp), v => ed.SetPercent(i, v));
                    PowerRow(new Rect(r.x + 24, rowY + 108, rowW, 46), L.Get("to"), rb.To.ToPercent(ftp), v => ed.SetPercent(i, v, rampEnd: true));
                    break;
                case FreeRideBlock f:
                    DurationRow(new Rect(r.x + 24, rowY, rowW, 46), i, f.DurationS);
                    GUI.Label(new Rect(r.x + 24, rowY + 60, rowW, 50), L.Get("free_hint"), Theme.BodyMuted);
                    break;
                case RepeatBlock rep:
                    GUI.Label(new Rect(r.x + 24, rowY, 140, 50), L.Get("repeats"), Theme.Body);
                    int count = Ui.Stepper(new Rect(r.x + 164, rowY, rowW - 140, 50), rep.Count, "×", 1, 1, 1, 50);
                    if (count != rep.Count) ed.SetRepeatCount(i, count);
                    GUI.Label(new Rect(r.x + 24, rowY + 60, rowW, 40), $"{rep.Blocks.Count} {L.Get("blocks")} · {Ui.Clock(rep.TotalDurationS)}", Theme.BodyMuted);
                    break;
            }

            // Actions (right)
            float ax = r.xMax - 24 - 360;
            float ay = r.y + 36;
            if (Ui.IconButton(new Rect(ax, ay, 52, 52), Icon.ChevronLeft, ButtonKind.Secondary, i > 0)) ed.Move(i, i - 1);
            if (Ui.IconButton(new Rect(ax + 60, ay, 52, 52), Icon.ChevronRight, ButtonKind.Secondary, i < ed.Blocks.Count - 1)) ed.Move(i, i + 1);
            if (Ui.Button(new Rect(ax + 124, ay, 236, 52), L.Get("duplicate"), ButtonKind.Secondary, Icon.Copy)) ed.Duplicate();
            if (Ui.Button(new Rect(ax + 124, ay + 60, 236, 52), L.Get("delete"), ButtonKind.Danger, Icon.Trash)) ed.Delete();
            if (block is RepeatBlock && Ui.Button(new Rect(ax, ay + 120, 360, 52), L.Get("edit_intervals"), ButtonKind.Primary, Icon.Edit)) ed.OpenSelectedRepeat();
        }

        private void DurationRow(Rect r, int index, int seconds)
        {
            var ed = _editor;
            GUI.Label(new Rect(r.x, r.y, 140, r.height), L.Get("duration"), Theme.Body);
            Steps(new Rect(r.x + 140, r.y, r.width - 140, r.height), Ui.Clock(seconds),
                ("−1m", () => ed.SetDuration(index, seconds - 60)),
                ("−15s", () => ed.SetDuration(index, seconds - 15)),
                ("+15s", () => ed.SetDuration(index, seconds + 15)),
                ("+1m", () => ed.SetDuration(index, seconds + 60)));
        }

        private void PowerRow(Rect r, string label, double percent, System.Action<double> set)
        {
            bool watts = UsesWatts(_editor.Workout);
            int ftp = _editor.FtpW;
            string value = watts ? $"{percent / 100.0 * ftp:0} W" : $"{percent:0} %";
            double small = watts ? 500.0 / ftp : 1; // 5 W or 1 %
            double large = watts ? 2500.0 / ftp : 5; // 25 W or 5 %
            string s = watts ? "5" : "1", l = watts ? "25" : "5";
            GUI.Label(new Rect(r.x, r.y, 140, r.height), label, Theme.Body);
            Steps(new Rect(r.x + 140, r.y, r.width - 140, r.height), value,
                ("−" + l, () => set(percent - large)),
                ("−" + s, () => set(percent - small)),
                ("+" + s, () => set(percent + small)),
                ("+" + l, () => set(percent + large)));
        }

        /// <summary>Value in the middle, half of the buttons on each side.</summary>
        private static void Steps(Rect r, string value, params (string Text, System.Action Action)[] buttons)
        {
            int half = buttons.Length / 2;
            float bw = 72;
            for (int b = 0; b < half; b++)
            {
                if (Ui.Button(new Rect(r.x + (b * (bw + 6)), r.y, bw, r.height), buttons[b].Text)) buttons[b].Action();
            }

            float right = r.xMax;
            for (int b = buttons.Length - 1; b >= half; b--)
            {
                right -= bw;
                if (Ui.Button(new Rect(right, r.y, bw, r.height), buttons[b].Text)) buttons[b].Action();
                right -= 6;
            }

            float left = r.x + (half * (bw + 6));
            GUI.Label(new Rect(left, r.y, right - left, r.height), value, Theme.With(Theme.H2, anchor: TextAnchor.MiddleCenter));
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using FreeVelo.App;
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
            var area = new Rect(20, 20, _w - 40, _h - 40);
            GUI.Box(area, GUIContent.none, _panel);

            // ---- top bar
            float x = area.x + 15, y = area.y + 12;
            GUI.Label(new Rect(x, y + 8, 90, 40), L.Get("name") + ":", _label);
            string name = GUI.TextField(new Rect(x + 90, y + 4, 380, 50), _editorName ?? string.Empty, 60, _textField);
            if (name != _editorName)
            {
                _editorName = name;
                ed.Rename(name);
            }

            float bx = area.xMax - 15;
            bx -= 150;
            if (GUI.Button(new Rect(bx, y, 150, 56), _confirmDiscard ? L.Get("discard") : L.Get("close"), _button))
            {
                if (!ed.IsDirty || _confirmDiscard) CloseEditor();
                else _confirmDiscard = true;
                return;
            }

            if (_editingBuiltIn)
            {
                bx -= 190;
                if (GUI.Button(new Rect(bx, y, 182, 56), L.Get("save_copy"), _button)) SaveEditor(asCopy: true);
            }

            bx -= 150;
            if (GUI.Button(new Rect(bx, y, 142, 56), L.Get("save"), _button)) SaveEditor(asCopy: false);
            bool watts = UsesWatts(ed.Workout);
            bx -= 110;
            if (GUI.Button(new Rect(bx, y, 102, 56), watts ? "W" : "%", _button)) ed.SetUnit(!watts);
            bx -= 80;
            GUI.enabled = ed.CanRedo;
            if (GUI.Button(new Rect(bx, y, 72, 56), "↷", _button)) ed.Redo();
            bx -= 80;
            GUI.enabled = ed.CanUndo;
            if (GUI.Button(new Rect(bx, y, 72, 56), "↶", _button)) ed.Undo();
            GUI.enabled = true;

            // ---- info line
            y += 64;
            string info = $"{L.Get("total")}: {FormatTime(ed.Workout.TotalDurationS)}   TSS ≈ {ed.EstimatedTss():0}   FTP {ed.FtpW} W";
            if (ed.IsDirty) info += "   •";
            if (!string.IsNullOrEmpty(_editorMessage)) info += "    " + _editorMessage;
            GUI.Label(new Rect(x, y, area.width - 30, 30), info, _small);

            // ---- palette
            y += 32;
            var paletteRects = new List<Rect>();
            float pw = Mathf.Min(118, (area.width - 30 - (8 * 8)) / 9f);
            for (int i = 0; i < 9; i++)
            {
                if (i == IntervalItem && ed.OpenRepeat != null) break; // no nested repeats
                var r = new Rect(x + (i * (pw + 8)), y, pw, 58);
                paletteRects.Add(r);
                DrawPaletteItem(r, i);
            }

            // ---- breadcrumb inside a repeat block
            y += 66;
            if (ed.OpenRepeat != null)
            {
                GUI.Label(new Rect(x, y, 600, 34), $"{L.Get("intervals")} ×{ed.OpenRepeat.Count} — {L.Get("editing_intervals")}", _label);
                if (GUI.Button(new Rect(x + 620, y - 4, 200, 42), L.Get("done"), _button)) ed.CloseRepeat();
            }
            else
            {
                GUI.Label(new Rect(x, y, area.width - 30, 34), L.Get("editor_hint"), _small);
            }

            // ---- timeline
            y += 40;
            var timeline = new Rect(x, y, area.width - 30, 250);
            GUI.Box(timeline, GUIContent.none, _panel);
            DrawTimeline(timeline);
            HandleEditorInput(timeline, paletteRects);

            // ---- selected block panel
            DrawBlockPanel(new Rect(x, timeline.yMax + 10, area.width - 30, area.yMax - timeline.yMax - 22));
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
            var old = GUI.color;
            string text;
            if (item < Palette.Length)
            {
                GUI.color = ZoneColor(Palette[item].Percent / 100.0 * AppSettings.FtpW, AppSettings.FtpW);
                text = $"{Palette[item].Label}\n<size=15>{Palette[item].Percent:0} %</size>";
            }
            else
            {
                GUI.color = new Color(0.45f, 0.5f, 0.58f);
                text = item == RampItem ? L.Get("ramp") : item == FreeItem ? L.Get("free") : L.Get("intervals");
            }

            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
            GUI.Label(r, text, _paletteLabel);
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
            GUI.color = new Color(1, 1, 1, 0.25f);
            float ftpY = PercentToY(t, 100);
            GUI.DrawTexture(new Rect(t.x + 4, ftpY, t.width - 8, 1), Texture2D.whiteTexture);
            GUI.color = old;
            GUI.Label(new Rect(t.xMax - 90, ftpY - 22, 80, 22), "FTP", _small);

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
                    DrawOutline(new Rect(rect.x, t.y + 4, rect.width, t.height - 8), new Color(1f, 0.85f, 0.2f), 3);
                    DrawHandles(t, rect, block);
                }

                x += width;
            }

            if (_dropIndex >= 0)
            {
                float dropX = _dropIndex < _blockRects.Count ? _blockRects[_dropIndex].Rect.x : x;
                GUI.color = new Color(1f, 0.85f, 0.2f);
                GUI.DrawTexture(new Rect(dropX - 2, t.y + 4, 4, t.height - 8), Texture2D.whiteTexture);
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
                    GUI.color = new Color(0.55f, 0.58f, 0.62f, 0.75f * tint.a);
                    GUI.DrawTexture(new Rect(rect.x + 1, top, rect.width - 2, t.yMax - 8 - top), Texture2D.whiteTexture);
                    GUI.color = tint;
                    GUI.Label(new Rect(rect.x, top - 26, rect.width, 24), f.IsMaxEffort ? "MAX" : L.Get("free"), _tinyCenter);
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
                                GUI.color = new Color(0.55f, 0.58f, 0.62f, 0.75f * tint.a);
                                GUI.DrawTexture(new Rect(cx, PercentToY(t, 75), w - 1, t.yMax - 8 - PercentToY(t, 75)), Texture2D.whiteTexture);
                                break;
                        }

                        cx += w;
                    }

                    GUI.color = new Color(1, 1, 1, 0.8f * tint.a);
                    GUI.DrawTexture(new Rect(rect.x + 2, t.y + 8, rect.width - 4, 3), Texture2D.whiteTexture);
                    GUI.color = tint;
                    GUI.Label(new Rect(rect.x, t.y + 12, rect.width, 26), $"×{rep.Count}", _tinyCenter);
                    break;
                }
            }

            GUI.color = tint;
            if (rect.width > 46) GUI.Label(new Rect(rect.x, t.yMax - 30, rect.width, 24), FormatTime(block.TotalDurationS), _tinyCenter);

            void Bar(float bx, float bw, double fromPct, double toPct)
            {
                int steps = Mathf.Abs((float)(toPct - fromPct)) < 0.5 ? 1 : Mathf.Max(1, Mathf.CeilToInt(bw / 5));
                for (int i = 0; i < steps; i++)
                {
                    double pct = fromPct + ((toPct - fromPct) * (i + 0.5) / steps);
                    float top = PercentToY(t, pct);
                    var c = ZoneColor(pct / 100.0 * ftp, ftp);
                    GUI.color = new Color(c.r, c.g, c.b, tint.a);
                    GUI.DrawTexture(new Rect(bx + (i * bw / steps), top, Mathf.Max(1, (bw / steps) - (steps == 1 ? 1 : 0)), t.yMax - 8 - top), Texture2D.whiteTexture);
                }

                GUI.color = tint;
                if (bw > 40) GUI.Label(new Rect(bx, PercentToY(t, Mathf.Max((float)fromPct, (float)toPct)) - 24, bw, 22), PowerText(fromPct, toPct), _tinyCenter);
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
            var c = new Color(1f, 0.85f, 0.2f);
            foreach (var h in Handles(t, rect, block)) DrawKnob(h.Rect, c);
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
            var old = GUI.color;
            GUI.color = new Color(0, 0, 0, 0.6f);
            GUI.DrawTexture(new Rect(r.x - 1, r.y - 1, r.width + 2, r.height + 2), Texture2D.whiteTexture);
            GUI.color = c;
            GUI.DrawTexture(new Rect(r.x + 3, r.y + 3, r.width - 6, r.height - 6), Texture2D.whiteTexture);
            GUI.color = old;
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
            GUILayout.BeginArea(r);
            if (block == null)
            {
                GUILayout.Label(L.Get("select_block"), _label);
                GUILayout.EndArea();
                return;
            }

            int i = ed.Selected;
            int ftp = ed.FtpW;
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(r.width * 0.62f));
            switch (block)
            {
                case SteadyBlock s:
                    DurationRow(i, s.DurationS);
                    PowerRow(L.Get("power"), s.Target.ToPercent(ftp), v => ed.SetPercent(i, v));
                    break;
                case RampBlock rb:
                    DurationRow(i, rb.DurationS);
                    PowerRow(L.Get("from"), rb.From.ToPercent(ftp), v => ed.SetPercent(i, v));
                    PowerRow(L.Get("to"), rb.To.ToPercent(ftp), v => ed.SetPercent(i, v, rampEnd: true));
                    break;
                case FreeRideBlock f:
                    DurationRow(i, f.DurationS);
                    GUILayout.Label(L.Get("free_hint"), _small);
                    break;
                case RepeatBlock rep:
                    EditorRow(L.Get("repeats"), $"×{rep.Count}", ("−1", () => ed.SetRepeatCount(i, rep.Count - 1)), ("+1", () => ed.SetRepeatCount(i, rep.Count + 1)));
                    GUILayout.Label($"{rep.Blocks.Count} {L.Get("blocks")} · {FormatTime(rep.TotalDurationS)}", _small);
                    break;
            }

            GUILayout.EndVertical();

            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("◀", _button, GUILayout.Height(50))) ed.Move(i, i - 1);
            if (GUILayout.Button("▶", _button, GUILayout.Height(50))) ed.Move(i, i + 1);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(L.Get("duplicate"), _button, GUILayout.Height(50))) ed.Duplicate();
            if (GUILayout.Button(L.Get("delete"), _button, GUILayout.Height(50))) ed.Delete();
            GUILayout.EndHorizontal();
            if (block is RepeatBlock && GUILayout.Button(L.Get("edit_intervals"), _button, GUILayout.Height(50))) ed.OpenSelectedRepeat();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DurationRow(int index, int seconds)
        {
            var ed = _editor;
            EditorRow(
                L.Get("duration"),
                FormatTime(seconds),
                ("−1m", () => ed.SetDuration(index, seconds - 60)),
                ("−15s", () => ed.SetDuration(index, seconds - 15)),
                ("+15s", () => ed.SetDuration(index, seconds + 15)),
                ("+1m", () => ed.SetDuration(index, seconds + 60)));
        }

        private void PowerRow(string label, double percent, System.Action<double> set)
        {
            bool watts = UsesWatts(_editor.Workout);
            int ftp = _editor.FtpW;
            string value = watts ? $"{percent / 100.0 * ftp:0} W" : $"{percent:0} %";
            double small = watts ? 500.0 / ftp : 1; // 5 W or 1 %
            double large = watts ? 2500.0 / ftp : 5; // 25 W or 5 %
            string s = watts ? "5" : "1", l = watts ? "25" : "5";
            EditorRow(
                label,
                value,
                ("−" + l, () => set(percent - large)),
                ("−" + s, () => set(percent - small)),
                ("+" + s, () => set(percent + small)),
                ("+" + l, () => set(percent + large)));
        }

        private void EditorRow(string label, string value, params (string Text, System.Action Action)[] buttons)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(54));
            GUILayout.Label(label, _label, GUILayout.Width(150));
            int half = buttons.Length / 2;
            for (int b = 0; b < half; b++)
            {
                if (GUILayout.Button(buttons[b].Text, _button, GUILayout.Width(82), GUILayout.Height(50))) buttons[b].Action();
            }

            GUILayout.Label(value, _labelCenter, GUILayout.Width(130));
            for (int b = half; b < buttons.Length; b++)
            {
                if (GUILayout.Button(buttons[b].Text, _button, GUILayout.Width(82), GUILayout.Height(50))) buttons[b].Action();
            }

            GUILayout.EndHorizontal();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using FreeVelo.App;
using FreeVelo.Ble;
using FreeVelo.Core.Training;
using FreeVelo.Core.Ui;
using FreeVelo.Core.Workouts;
using FreeVelo.Devices.Session;
using FreeVelo.Ride;
using UnityEngine;

namespace FreeVelo.UI
{
    /// <summary>
    /// FreeVelo user interface (IMGUI with the <see cref="Theme"/> / <see cref="Ui"/> design system), laid out on a
    /// virtual canvas of 720 units height that is scaled to the screen. Menus: navigation rail on the left with
    /// Ride, Workouts, Devices and Settings; the 3D rider stays visible beside the content.
    /// </summary>
    public sealed partial class Hud : MonoBehaviour
    {
        private enum Page
        {
            Home,
            Group,
            Workouts,
            Training,
            Devices,
            Settings,
            Editor,
            RideDetail,
        }

        private enum WorkoutTab
        {
            Library,
            Mine,
            Tests,
        }

        private const float RefH = 720f;
        private const float RailW = 108f;

        private BleService _ble;
        private RideController _ride;
        private Page _page = Page.Home;
        private WorkoutTab _workoutTab;
        private float _w;
        private float _h;
        private Rect _full;
        private readonly Dictionary<Workout, (int Ftp, double Tss, IReadOnlyList<WorkoutSegment> Segments)> _workoutInfo =
            new Dictionary<Workout, (int, double, IReadOnlyList<WorkoutSegment>)>();

        private string _confirmDelete;
        private string _confirmReset;
        private bool _confirmEnd;
        private bool _showLog;

        public void Initialize(BleService ble, RideController ride)
        {
            _ble = ble;
            _ride = ride;
        }

        private void OnGUI()
        {
            if (_ble == null || _ride == null) return;
            Theme.Ensure();

            // Layout inside the safe area (camera hole, rounded corners); height-based scale, narrow (4:3)
            // tablets get at least 1152 units width. _full covers the whole screen for backgrounds.
            var safe = Screen.safeArea;
            float scale = Mathf.Min(safe.height / RefH, safe.width / 1152f);
            _w = safe.width / scale;
            _h = safe.height / scale;
            float offsetX = safe.x, offsetY = Screen.height - safe.yMax;
            _full = new Rect(-offsetX / scale, -offsetY / scale, Screen.width / scale, Screen.height / scale);
            GUI.matrix = Matrix4x4.TRS(new Vector3(offsetX, offsetY, 0), Quaternion.identity, new Vector3(scale, scale, 1));
            _guiScale = scale;
            _guiOffset = new Vector2(offsetX, offsetY);

            if (!string.IsNullOrEmpty(WorkoutStore.LastMessage))
            {
                Ui.Toast(WorkoutStore.LastMessage);
                WorkoutStore.LastMessage = null;
            }

            // Start-up: the loading screen until the world is built (the menus come in as it fades out).
            if (_world != null && !_world.IsLoaded)
            {
                DrawLoadingScreen();
                return;
            }

            switch (_ride.State)
            {
                case RideState.Riding:
                    DrawRide();
                    break;
                case RideState.Finished:
                    DrawSummary();
                    break;
                default:
                    if (_page == Page.Editor)
                    {
                        if (_editor != null) DrawEditor();
                        else _page = Page.Workouts;
                    }
                    else
                    {
                        DrawMenu();
                    }

                    break;
            }

            DrawLoadingScreen(); // fading out
            Ui.DrawToast(_w, _h);
            DrawFriendRequests();
            Ui.DrawKeyboardBar(_w);
        }

        // ------------------------------------------------------------------ navigation

        private void Navigate(Page page)
        {
            if (_page == Page.Settings && page != Page.Settings) _ble.ApplySettings();
            _page = page;
            _confirmDelete = null;
            _confirmReset = null;
        }

        private void DrawMenu()
        {
            // Content background (the home screen leaves the right side to the 3D rider).
            float contentRight = _page == Page.Home ? Mathf.Min(RailW + 800, _w * 0.62f) : _w;
            Ui.Rect(new Rect(_full.x, _full.y, contentRight - _full.x, _full.height), Theme.Alpha(Theme.Background, 0.9f), 0);
            if (_page == Page.Home) Ui.Fade(new Rect(contentRight, _full.y, 160, _full.height), Theme.Alpha(Theme.Background, 0.9f), 0);

            DrawRail();
            var content = new Rect(RailW + 32, 28, contentRight - RailW - 64, _h - 56);
            switch (_page)
            {
                case Page.Home: DrawHome(content); break;
                case Page.Group: DrawGroup(content); break;
                case Page.Workouts: DrawWorkouts(content); break;
                case Page.Devices: DrawDevices(content); break;
                case Page.Training: DrawTraining(content); break;
                case Page.RideDetail: DrawRideDetail(content); break;
                case Page.Settings: DrawSettings(content); break;
            }
        }

        private Texture2D _logoBike;

        private void DrawRail()
        {
            Ui.Rect(new Rect(_full.x, _full.y, RailW - _full.x, _full.height), Theme.Background, 0);
            Ui.Rect(new Rect(RailW - 1, _full.y, 1, _full.height), Theme.Stroke, 0);

            // Logo
            var logo = new Rect((RailW - 56) / 2f, 24, 56, 56);
            Ui.Rect(logo, Theme.Accent, 16);
            _logoBike ??= Resources.Load<Texture2D>("FreeVeloLogoBike"); // the bike of the app icon
            if (_logoBike != null) GUI.DrawTexture(new Rect(logo.x + 4, logo.y + 4, logo.width - 8, logo.height - 8), _logoBike, ScaleMode.ScaleToFit, true);
            else Ui.DrawIcon(new Rect(logo.x + 9, logo.y + 9, logo.width - 18, logo.height - 18), Icon.Bike, Color.white);

            var items = new (Page Page, Icon Icon, string Label)[]
            {
                (Page.Home, Icon.Bike, L.Get("ride")),
                (Page.Group, Icon.Group, L.Get("group")),
                (Page.Workouts, Icon.List, L.Get("workouts")),
                (Page.Training, Icon.Target, L.Get("training")),
                (Page.Devices, Icon.Bluetooth, L.Get("devices")),
                (Page.Settings, Icon.Gear, L.Get("settings")),
            };
            float y = 120;
            foreach (var item in items)
            {
                var r = new Rect(8, y, RailW - 16, 80);
                bool selected = _page == item.Page || (_page == Page.RideDetail && item.Page == Page.Training);
                var pill = new Rect(r.center.x - 30, r.y + 8, 60, 38);
                if (selected) Ui.Rect(pill, Theme.Alpha(Theme.Accent, 0.18f), 19);
                Ui.DrawIcon(new Rect(pill.center.x - 13, pill.center.y - 13, 26, 26), item.Icon, selected ? Theme.Accent : Theme.Muted);
                GUI.Label(new Rect(r.x, r.y + 50, r.width, 24), item.Label, Theme.With(Theme.Caption, selected ? Theme.Text : Theme.Muted, 14, TextAnchor.MiddleCenter));
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) Navigate(item.Page);
                y += 82;
            }

            // Connection indicator at the bottom: trainer status.
            bool trainer = _ble.Hub.TrainerAddress != null;
            Ui.Dot(new Vector2(RailW / 2f, _h - 40), 6, trainer ? Theme.Success : Theme.Faint);
        }

        private static void Header(Rect content, string title, string subtitle = null)
        {
            GUI.Label(new Rect(content.x, content.y, content.width, 44), title, Theme.H1);
            if (subtitle != null) GUI.Label(new Rect(content.x, content.y + 44, content.width, 26), subtitle, Theme.BodyMuted);
        }

        // ------------------------------------------------------------------ home

        private void DrawHome(Rect page)
        {
            // Hero, quick start, devices (~628) and the climbs below: scrolls on small screens.
            var view = new Rect(page.x - 8, page.y, page.width + 16, page.height);
            float contentH = 628 + 24 + ClimbSectionH + 16;
            Ui.BeginScroll(900, view, contentH);
            DrawHomeContent(new Rect(8, 0, page.width, contentH));
            Ui.EndScroll(900, view, contentH);
        }

        private void DrawHomeContent(Rect c)
        {
            var route = _ride.Route;
            Header(c, L.Get("ride"));
            float wkg = AppSettings.FtpW / Mathf.Max(1f, AppSettings.RiderWeightKg);
            float chipX = c.x;
            chipX += Ui.Chip(new Rect(chipX, c.y + 50, 0, 32), $"FTP {AppSettings.FtpW} W", Theme.Accent, Icon.Bolt) + 8;
            Ui.Chip(new Rect(chipX, c.y + 50, 0, 32), $"{wkg:0.0} W/kg · {AppSettings.RiderWeightKg:0} kg", Theme.Muted);

            // Hero: free ride on the current route.
            var hero = new Rect(c.x, c.y + 100, c.width, 236);
            Ui.Card(hero);
            GUI.Label(new Rect(hero.x + 24, hero.y + 20, 400, 20), L.Get("free_ride").ToUpperInvariant(), Theme.Overline);
            GUI.Label(new Rect(hero.x + 24, hero.y + 42, hero.width - 48, 34), L.Get("world_name"), Theme.H2);
            var map = _ride.Map;
            if (map != null)
            {
                var loop = map.MainLoop;
                double network = 0;
                foreach (var edge in map.Edges) network += edge.Length;
                float x = hero.x + 24;
                x += Ui.Chip(new Rect(x, hero.y + 82, 0, 30), $"{L.Get("main_loop")} {loop.LengthM / 1000:0.0} km", Theme.Info, Icon.Route) + 8;
                x += Ui.Chip(new Rect(x, hero.y + 82, 0, 30), $"{loop.TotalAscentM:0} m ↑", Theme.Success) + 8;
                Ui.Chip(new Rect(x, hero.y + 82, 0, 30), string.Format(L.Get("network_info"), network / 1000, map.Nodes.Count), Theme.Muted, Icon.Signal);
                DrawElevation(new Rect(hero.x + 24, hero.y + 124, hero.width - 236, 88), loop, -1);
            }

            if (_group?.Lobby != null)
            {
                // In a group ride: starting here (or a workout below) rides with the group.
                Ui.Chip(new Rect(hero.xMax - 196, hero.yMax - 118, 0, 30), _group.Lobby.name, Theme.Accent, Icon.Group, filled: true);
            }

            if (Ui.Button(new Rect(hero.xMax - 196, hero.yMax - 76, 172, 56), L.Get("start"), ButtonKind.Primary, Icon.Play)) _ride.StartRide(null);

            // Quick start: the training plan's next session (or a workout that suits the form), then the workouts
            // ridden last.
            float y = hero.yMax + 26;
            GUI.Label(new Rect(c.x, y, 400, 20), L.Get("quick_start").ToUpperInvariant(), Theme.Overline);
            var cards = QuickStartCards();
            float cw = (c.width - (16 * (cards.Count - 1))) / Mathf.Max(1, cards.Count);
            for (int i = 0; i < cards.Count; i++) DrawQuickCard(new Rect(c.x + (i * (cw + 16)), y + 26, cw, 150), cards[i]);

            // Devices status
            var dev = new Rect(c.x, y + 194, c.width, 72);
            Ui.Card(dev);
            var hub = _ble.Hub;
            var roles = new (Icon Icon, string Label, string Address)[]
            {
                (Icon.Trainer, L.Get("trainer"), hub.TrainerAddress),
                (Icon.Bolt, L.Get("power_meter"), hub.PowerMeterAddress),
                (Icon.Heart, L.Get("heart_rate"), hub.HeartRateSource),
            };
            float rw = (dev.width - 150) / 3f;
            for (int i = 0; i < roles.Length; i++)
            {
                float x = dev.x + 20 + (i * rw);
                bool ok = roles[i].Address != null;
                Ui.DrawIcon(new Rect(x, dev.center.y - 12, 24, 24), roles[i].Icon, ok ? Theme.Text : Theme.Faint);
                Ui.Dot(new Vector2(x + 24, dev.center.y - 12), 5, ok ? Theme.Success : Theme.Faint);
                GUI.Label(new Rect(x + 36, dev.center.y - 12, rw - 44, 24), ok ? _ble.NameOf(roles[i].Address) : roles[i].Label, Clip(Theme.With(ok ? Theme.Body : Theme.BodyMuted, size: 15)));
            }

            if (Ui.Button(new Rect(dev.xMax - 140, dev.center.y - 22, 124, 44), L.Get("manage"), ButtonKind.Secondary)) Navigate(Page.Devices);

            DrawClimbCards(c.x, dev.yMax + 24, c.width);
        }

        /// <summary>Width of one column of an elevation profile (GUI units).</summary>
        private const float ProfileColumnW = 3f;

        /// <summary>
        /// Elevation samples every <paramref name="step"/> metres from <paramref name="start"/> (<paramref name="count"/> of
        /// them): each the mean of its stretch (4 samples), then evened out with its neighbours (1-2-1).
        /// </summary>
        private static double[] ProfileSamples(FreeVelo.Core.World.Route route, double start, double step, int count, double clampAt)
        {
            const int samples = 4;
            var raw = new double[count];
            for (int i = 0; i < count; i++)
            {
                double sum = 0;
                for (int k = 0; k < samples; k++) sum += route.Sample(System.Math.Min(clampAt, start + (step * (i - 0.5 + ((k + 0.5) / samples))))).Y;
                raw[i] = sum / samples;
            }

            var e = new double[count];
            for (int i = 0; i < count; i++) e[i] = (raw[System.Math.Max(0, i - 1)] + (2 * raw[i]) + raw[System.Math.Min(count - 1, i + 1)]) / 4;
            return e;
        }

        /// <summary>Elevation at a distance, interpolated between the samples (start, step).</summary>
        private static double SampleAt(double[] e, double start, double step, double d)
        {
            double f = (d - start) / step;
            int i = Mathf.Clamp((int)System.Math.Floor(f), 0, e.Length - 1);
            int j = System.Math.Min(e.Length - 1, i + 1);
            double t = System.Math.Max(0, System.Math.Min(1, f - i));
            return e[i] + ((e[j] - e[i]) * t);
        }

        /// <summary>
        /// Draws an elevation profile as thin solid columns (no seams, no overlapping see-through bars): ridden part orange
        /// left of <paramref name="rider"/>, the rest blue with <paramref name="alphaAt"/> (fainter where the way can still
        /// change). Heights are interpolated from samples tied to the road, so the outline glides smoothly when it scrolls.
        /// </summary>
        private static void ProfileColumns(Rect r, double from, double to, double[] e, double start, double step, double min, double span,
            double rider, System.Func<double, float> alphaAt)
        {
            int cols = Mathf.Max(1, Mathf.FloorToInt(r.width / ProfileColumnW));
            float cw = r.width / cols;
            // Opaque colours (blended over the card): neighbouring columns never double up into lighter lines.
            var bg = Theme.Background;
            var done = Color.Lerp(bg, Theme.Accent, 0.78f);
            float px = r.x + (r.width * (float)((rider - from) / (to - from)));
            for (int c = 0; c < cols; c++)
            {
                double d = from + ((to - from) * (c + 0.5) / cols);
                float h = 8 + (float)((SampleAt(e, start, step, d) - min) / span) * (r.height - 8);
                float x0 = r.x + (c * cw), x1 = x0 + cw;
                var ahead = Color.Lerp(bg, Theme.Info, alphaAt(d));
                float split = Mathf.Clamp(px, x0, x1);
                if (split > x0) Ui.Rect(new Rect(x0, r.yMax - h, split - x0, h), done, 0);
                if (split < x1) Ui.Rect(new Rect(split, r.yMax - h, x1 - split, h), ahead, 0);
            }
        }

        /// <summary>Height range a whole-round profile shows at least, so small hills don't look like mountains.</summary>
        private const double MinRoundSpanM = 100;

        /// <summary>Same for the "next 3 km" profile.</summary>
        private const double MinAheadSpanM = 60;

        /// <summary>Metres up and down between two distances (sampled every 10 m).</summary>
        private static (double Up, double Down) Climbing(FreeVelo.Core.World.Route route, double from, double to)
        {
            double up = 0, down = 0, last = route.Sample(from).Y;
            for (double d = from + 10; d <= to; d += 10)
            {
                double y = route.Sample(d).Y;
                if (y > last) up += y - last;
                else down += last - y;
                last = y;
            }

            return (up, down);
        }

        private FreeVelo.Core.World.Route _roundProfileRoute;
        private double[] _roundProfile;

        private void DrawElevation(Rect r, FreeVelo.Core.World.Route route, double position)
        {
            // The whole round: samples worked out once per route (about one per column).
            const int count = 240;
            double step = route.LengthM / count;
            if (_roundProfileRoute != route)
            {
                _roundProfile = ProfileSamples(route, step / 2, step, count, double.MaxValue);
                _roundProfileRoute = route;
            }

            var e = _roundProfile;
            double min = e.Min(), max = e.Max();
            double span = System.Math.Max(MinRoundSpanM, max - min);
            ProfileColumns(r, 0, route.LengthM, e, step / 2, step, min, span, position >= 0 ? position : double.NegativeInfinity, _ => 0.4f);
            if (position >= 0)
            {
                float px = r.x + (r.width * (float)(position / route.LengthM));
                Ui.Rect(new Rect(px - 1.5f, r.y, 3, r.height), Theme.Text, 1);
            }
        }

        // ------------------------------------------------------------------ workouts

        private (int Ftp, double Tss, IReadOnlyList<WorkoutSegment> Segments) Info(Workout w)
        {
            int ftp = AppSettings.FtpW;
            if (!_workoutInfo.TryGetValue(w, out var info) || info.Ftp != ftp)
            {
                info = (ftp, WorkoutEditor.EstimateTss(w, ftp), w.Flatten());
                _workoutInfo[w] = info;
            }

            return info;
        }

        /// <summary>Duration and TSS (not for FTP tests: their length and load depend on the rider).</summary>
        private string Meta(Workout w) => w.Category == WorkoutCategory.FtpTest ? Ui.Minutes(w.TotalDurationS) : $"{Ui.Minutes(w.TotalDurationS)} · TSS {Info(w).Tss:0}";

        private static GUIStyle Clip(GUIStyle style)
        {
            style.wordWrap = false;
            style.clipping = TextClipping.Clip;
            return style;
        }

        /// <summary>Workout profile in zone colours; <paramref name="progressS"/> ≥ 0 marks the current position.</summary>
        private void WorkoutProfile(Rect r, Workout w, bool background, double progressS = -1)
        {
            if (background) Ui.Rect(r, Theme.Alpha(Theme.Background, 0.5f), Theme.RadiusSmall);
            var info = Info(w);
            int ftp = info.Ftp;
            float total = Mathf.Max(1, w.TotalDurationS);
            var inner = background ? new Rect(r.x + 8, r.y + 8, r.width - 16, r.height - 16) : r;
            float maxPct = 1.6f;
            foreach (var seg in info.Segments)
            {
                float x0 = inner.x + (inner.width * seg.StartS / total);
                float width = inner.width * seg.DurationS / total;
                int steps = seg.Block is RampBlock ? Mathf.Max(1, Mathf.CeilToInt(width / 4)) : 1;
                for (int s = 0; s < steps; s++)
                {
                    double? watts = seg.BaseTargetW(seg.DurationS * (s + 0.5) / steps, ftp);
                    float pct = watts.HasValue ? (float)(watts.Value / ftp) : 0.75f;
                    float h = Mathf.Clamp(pct / maxPct, 0.06f, 1f) * inner.height;
                    var col = watts.HasValue ? ZoneColor(watts.Value, ftp) : Theme.Faint;
                    bool past = progressS >= 0 && seg.StartS + (seg.DurationS * (s + 1f) / steps) <= progressS;
                    Ui.Rect(new Rect(x0 + (s * width / steps), inner.yMax - h, Mathf.Max(1, (width / steps) + (steps == 1 ? -1 : 0.7f)), h), Theme.Alpha(col, past ? 0.45f : 1f), steps == 1 ? 2 : 0);
                }
            }

            if (progressS >= 0)
            {
                float px = inner.x + (inner.width * (float)(progressS / total));
                Ui.Rect(new Rect(px - 1.5f, r.y + 2, 3, r.height - 4), Theme.Text, 1.5f);
            }
        }

        private Color ZoneColor(double watts, int ftp)
        {
            var zone = AppSettings.Zones.ZoneFor(watts, ftp);
            return ColorUtility.TryParseHtmlString(zone.ColorHex, out var c) ? c : Color.gray;
        }

        private void DrawWorkouts(Rect c)
        {
            Header(c, L.Get("workouts"));
            var tabs = new[] { L.Get("templates"), L.Get("my_workouts"), L.Get("ftp_tests") };
            var tabRect = new Rect(c.x + 220, c.y + 2, Mathf.Clamp(c.width - 600, 320, 520), 46);
            var tab = (WorkoutTab)Ui.Segmented(tabRect, tabs, (int)_workoutTab);
            if (tab != _workoutTab)
            {
                _workoutTab = tab;
                _confirmDelete = null;
                _confirmReset = null;
            }

            if (_workoutTab == WorkoutTab.Mine)
            {
                float nw = Ui.ButtonWidth(L.Get("new"), true, 50), iw = Ui.ButtonWidth(L.Get("import"), true, 50);
                if (Ui.Button(new Rect(c.xMax - nw, c.y, nw, 50), L.Get("new"), ButtonKind.Primary, Icon.Plus)) OpenEditor(null, false);
                if (Ui.Button(new Rect(c.xMax - nw - 10 - iw, c.y, iw, 50), L.Get("import"), ButtonKind.Secondary, Icon.Import)) WorkoutStore.PickFiles();
            }

            var library = WorkoutStore.Library;
            IReadOnlyList<Workout> list = _workoutTab == WorkoutTab.Library ? library.Templates
                : _workoutTab == WorkoutTab.Mine ? library.Workouts
                : new[] { FtpTests.Create(FtpTestKind.Ramp), FtpTests.Create(FtpTestKind.TwentyMinute), FtpTests.Create(FtpTestKind.TwoByEight) };

            var view = new Rect(c.x - 8, c.y + 70, c.width + 16, c.height - 70);
            if (list.Count == 0)
            {
                var empty = new Rect(c.x, view.y + 20, c.width, 160);
                Ui.Card(empty);
                Ui.DrawIcon(new Rect(empty.x + 28, empty.center.y - 24, 48, 48), Icon.Import, Theme.Muted);
                GUI.Label(new Rect(empty.x + 100, empty.y, empty.width - 130, empty.height), L.Get("no_own_workouts"), Theme.Body);
                return;
            }

            int columns = Mathf.Clamp(Mathf.FloorToInt((c.width + 20) / 360f), 1, 4);
            float cw = (c.width - (20 * (columns - 1))) / columns;
            float ch = _workoutTab == WorkoutTab.Tests ? 300 : 232;
            int rows = (list.Count + columns - 1) / columns;
            float contentH = (rows * (ch + 20)) + 20;

            Ui.BeginScroll(1000 + (int)_workoutTab, view, contentH);
            for (int i = 0; i < list.Count; i++)
            {
                var r = new Rect(8 + ((i % columns) * (cw + 20)), 8 + ((i / columns) * (ch + 20)), cw, ch);
                WorkoutCard(r, list[i]);
            }

            Ui.EndScroll(1000 + (int)_workoutTab, view, contentH);
            Ui.Fade(new Rect(view.x, view.yMax - 36, view.width, 36), Theme.Alpha(Theme.Background, 0.9f), 2);
        }

        private void WorkoutCard(Rect r, Workout w)
        {
            var library = WorkoutStore.Library;
            Ui.Card(r);
            float x = r.x + 20;
            GUI.Label(new Rect(x, r.y + 16, r.width - 40, 28), w.Name.Get(AppSettings.Language), Theme.With(Theme.H3, size: 19));
            GUI.Label(new Rect(x, r.y + 44, r.width - 40, 22), Meta(w), Theme.Caption);

            bool modified = _workoutTab == WorkoutTab.Library && library.IsModified(w.Id);
            if (modified) Ui.Chip(new Rect(r.xMax - 124, r.y + 18, 0, 26), L.Get("modified"), Theme.Warning, Icon.Edit);

            float profileY = r.y + 76;
            if (_workoutTab == WorkoutTab.Tests)
            {
                var desc = Theme.With(Theme.BodyMuted, size: 15, anchor: TextAnchor.UpperLeft);
                GUI.Label(new Rect(x, r.y + 74, r.width - 40, 66), w.Description?.Get(AppSettings.Language) ?? string.Empty, desc);
                profileY = r.y + 146;
            }

            WorkoutProfile(new Rect(x, profileY, r.width - 40, _workoutTab == WorkoutTab.Tests ? 66 : 72), w, true);

            // Actions
            float by = r.yMax - 64;
            if (Ui.Button(new Rect(x, by, 132, 48), L.Get("start"), ButtonKind.Primary, Icon.Play)) _ride.StartRide(w);
            float ix = r.xMax - 20 - 48;
            switch (_workoutTab)
            {
                case WorkoutTab.Library:
                    if (modified && _confirmReset == w.Id)
                    {
                        // Confirming: only the confirm button next to Start, as wide as its text.
                        if (ConfirmButton(r, by, L.Get("confirm_reset"))) ResetTemplate(w);
                        break;
                    }

                    if (modified)
                    {
                        if (Ui.IconButton(new Rect(ix, by, 48, 48), Icon.Reset)) _confirmReset = w.Id;
                        ix -= 56;
                    }

                    if (Ui.IconButton(new Rect(ix, by, 48, 48), Icon.Edit)) OpenEditor(w, true);
                    break;

                case WorkoutTab.Mine:
                {
                    if (_confirmDelete == w.Id)
                    {
                        if (ConfirmButton(r, by, L.Get("confirm_delete")))
                        {
                            WorkoutStore.Delete(w);
                            _confirmDelete = null;
                        }

                        break;
                    }

                    if (Ui.IconButton(new Rect(ix, by, 48, 48), Icon.Trash)) _confirmDelete = w.Id;

                    ix -= 56;
                    if (Ui.IconButton(new Rect(ix, by, 48, 48), Icon.Share)) WorkoutStore.Share(w);
                    ix -= 56;
                    if (Ui.IconButton(new Rect(ix, by, 48, 48), Icon.Edit)) OpenEditor(w, false);
                    break;
                }
            }
        }

        /// <summary>Red confirm button at the right of a workout card, as wide as its text (at most up to the Start button).</summary>
        private static bool ConfirmButton(Rect card, float y, string text)
        {
            float w = Mathf.Min(Ui.ButtonWidth(text, false, 48), card.width - 40 - 132 - 12);
            return Ui.Button(new Rect(card.xMax - 20 - w, y, w, 48), text, ButtonKind.Danger);
        }

        private void ResetTemplate(Workout w)
        {
            WorkoutStore.Library.ResetToDefault(w.Id);
            Ui.Toast("✓ " + L.Get("reset_done") + ": " + w.Name.Get(AppSettings.Language));
            _confirmReset = null;
        }

        // ------------------------------------------------------------------ devices

        private void DrawDevices(Rect c)
        {
            Header(c, L.Get("devices"), _ble.BackendName);
            bool scanning = _ble.IsScanning;
            if (Ui.Button(new Rect(c.xMax - 190, c.y, 190, 50), scanning ? L.Get("stop_scan") : L.Get("scan"), scanning ? ButtonKind.Secondary : ButtonKind.Primary, scanning ? Icon.Stop : Icon.Search))
            {
                if (scanning) _ble.StopScan();
                else _ble.StartScan();
            }

            if (scanning)
            {
                float pulse = 0.5f + (0.5f * Mathf.Sin(Time.realtimeSinceStartup * 5f));
                Ui.Dot(new Vector2(c.xMax - 210, c.y + 25), 6, Theme.Alpha(Theme.Accent, 0.4f + (0.6f * pulse)));
            }

            // Your setup
            var hub = _ble.Hub;
            var roles = new (Icon Icon, string Label, string Address, string Detail)[]
            {
                (Icon.Trainer, L.Get("trainer"), hub.TrainerAddress, hub.TrainerAddress != null ? ProtocolName(hub.TrainerProtocol.ToString()) : null),
                (Icon.Bolt, L.Get("power_meter"), hub.PowerMeterAddress, null),
                (Icon.Heart, L.Get("heart_rate"), hub.HeartRateSource, null),
            };
            float rw = (c.width - 48) / 4f;
            DrawControllerCard(new Rect(c.x + (3 * (rw + 16)), c.y + 86, rw, 96));
            for (int i = 0; i < roles.Length; i++)
            {
                var r = new Rect(c.x + (i * (rw + 16)), c.y + 86, rw, 96);
                bool ok = roles[i].Address != null;
                Ui.Card(r);
                var circle = new Rect(r.x + 18, r.y + 22, 52, 52);
                Ui.Rect(circle, ok ? Theme.Alpha(Theme.Success, 0.16f) : Theme.Surface2, 26);
                Ui.DrawIcon(new Rect(circle.center.x - 13, circle.center.y - 13, 26, 26), roles[i].Icon, ok ? Theme.Success : Theme.Faint);
                GUI.Label(new Rect(r.x + 84, r.y + 20, r.width - 100, 18), roles[i].Label.ToUpperInvariant(), Theme.Overline);
                GUI.Label(new Rect(r.x + 84, r.y + 40, r.width - 100, 28), ok ? _ble.NameOf(roles[i].Address) : L.Get("not_connected"), ok ? Theme.With(Theme.H3, size: 18) : Theme.With(Theme.BodyMuted, size: 17));
                if (roles[i].Detail != null) GUI.Label(new Rect(r.x + 84, r.y + 66, r.width - 100, 20), roles[i].Detail, Theme.Caption);
            }

            // Nearby devices
            float y = c.y + 206;
            GUI.Label(new Rect(c.x, y, 400, 20), L.Get("nearby").ToUpperInvariant(), Theme.Overline);
            if (Ui.Button(new Rect(c.xMax - 150, y - 12, 150, 36), _showLog ? L.Get("hide_log") : L.Get("show_log"), ButtonKind.Ghost)) _showLog = !_showLog;

            var devices = _ble.Devices.ToList();
            float listBottom = _showLog ? c.yMax - 150 : c.yMax;
            var view = new Rect(c.x - 8, y + 30, c.width + 16, listBottom - y - 30);
            if (devices.Count == 0)
            {
                var empty = new Rect(c.x, view.y + 6, c.width, 96);
                Ui.Card(empty);
                Ui.DrawIcon(new Rect(empty.x + 24, empty.center.y - 18, 36, 36), Icon.Bluetooth, Theme.Muted);
                GUI.Label(new Rect(empty.x + 80, empty.y, empty.width - 100, empty.height), L.Get("no_devices"), Theme.Body);
            }
            else
            {
                const float rowH = 84;
                float contentH = (devices.Count * (rowH + 12)) + 12;
                Ui.BeginScroll(2000, view, contentH);
                for (int i = 0; i < devices.Count; i++) DeviceRow(new Rect(8, 6 + (i * (rowH + 12)), c.width, rowH), devices[i]);
                Ui.EndScroll(2000, view, contentH);
            }

            if (_showLog)
            {
                var log = new Rect(c.x, c.yMax - 136, c.width, 136);
                Ui.Rect(log, Theme.Surface, Theme.RadiusSmall);
                float ly = log.y + 10;
                foreach (var line in _ble.Log.Take(4))
                {
                    GUI.Label(new Rect(log.x + 16, ly, log.width - 32, 22), line, Theme.With(Theme.Caption, size: 14));
                    ly += 24;
                }

                string path = _ble.LogFilePath;
                if (!string.IsNullOrEmpty(path))
                {
                    // Android/data cannot be opened by other apps (Android 11+): share or copy the log from here.
                    GUI.Label(new Rect(log.x + 16, log.yMax - 28, log.width - 380, 20), System.IO.Path.GetFileName(path), Clip(Theme.With(Theme.Caption, Theme.Faint, 13)));
                    if (Ui.Button(new Rect(log.xMax - 356, log.yMax - 50, 170, 40), L.Get("share_log"), ButtonKind.Secondary, Icon.Share))
                    {
                        string error = FileActions.Share(path, L.Get("share_log"));
                        if (error != null) Ui.Toast(error);
                    }

                    if (Ui.Button(new Rect(log.xMax - 178, log.yMax - 50, 166, 40), L.Get("save_log"), ButtonKind.Secondary, Icon.Download))
                    {
                        Ui.Toast(FileActions.SaveToDownloads(path));
                    }
                }
            }
        }

        private static string ProtocolName(string protocol) =>
            protocol.Equals("ftms", System.StringComparison.OrdinalIgnoreCase) ? "FTMS"
            : protocol.ToLowerInvariant().Contains("fec") ? "Tacx FE-C"
            : protocol;

        private void DeviceRow(Rect r, DeviceInfo d)
        {
            Ui.Card(r);
            if (d.IsConnected) Ui.Rect(new Rect(r.x, r.y + 14, 4, r.height - 28), Theme.Success, 2);
            string tags = d.Tags;
            Icon icon = tags.Contains("FTMS") || tags.Contains("FE-C") ? Icon.Trainer
                : tags.Contains("HR") ? Icon.Heart
                : tags.Contains(L.Get("power")) ? Icon.Bolt
                : Icon.Bluetooth;
            var circle = new Rect(r.x + 16, r.center.y - 24, 48, 48);
            Ui.Rect(circle, d.IsConnected ? Theme.Alpha(Theme.Success, 0.16f) : Theme.Surface2, 24);
            Ui.DrawIcon(new Rect(circle.center.x - 12, circle.center.y - 12, 24, 24), icon, d.IsConnected ? Theme.Success : Theme.Text);

            string name = string.IsNullOrEmpty(d.Name) ? d.Address : d.Name;
            GUI.Label(new Rect(r.x + 80, r.y + 14, r.width - 400, 28), name, Theme.With(Theme.H3, size: 18));
            GUI.Label(new Rect(r.x + 80, r.y + 44, r.width - 400, 22), string.IsNullOrEmpty(tags) ? d.Address : tags, Theme.Caption);

            // Signal strength: 4 bars, coloured by RSSI.
            int bars = d.Rssi >= -60 ? 4 : d.Rssi >= -70 ? 3 : d.Rssi >= -80 ? 2 : 1;
            float sx = r.xMax - 300;
            for (int b = 0; b < 4; b++)
            {
                float h = 8 + (b * 5);
                Ui.Rect(new Rect(sx + (b * 8), r.center.y + 10 - h, 5, h), b < bars ? Theme.Text : Theme.Stroke, 1.5f);
            }

            if (d.State != null && !d.IsConnected) Ui.Chip(new Rect(sx + 44, r.center.y - 14, 0, 28), d.State, Theme.Warning);

            if (d.IsConnected)
            {
                if (Ui.Button(new Rect(r.xMax - 176, r.center.y - 24, 160, 48), L.Get("disconnect"), ButtonKind.Secondary, Icon.Close)) _ble.Disconnect(d.Address);
            }
            else if (Ui.Button(new Rect(r.xMax - 176, r.center.y - 24, 160, 48), L.Get("connect"), ButtonKind.Primary))
            {
                _ble.Connect(d.Address);
            }
        }

        // ------------------------------------------------------------------ settings

        private bool _confirmZoneReset;

        private void DrawSettings(Rect c)
        {
            Header(c, L.Get("settings"));
            var view = new Rect(c.x - 8, c.y + 64, c.width + 16, c.height - 64);
            float colW = (c.width - 24) / 2f;
            var zones = AppSettings.Zones;
            float zonesH = 92 + (zones.Zones.Count * 58);
            bool sim = _ble.BackendName == "Simulator";
            const float sectionH = 40, gap = 24;
            float contentH = 8
                + sectionH + 280 + gap + zonesH + gap + HrCardH + gap          // profile
                + sectionH + 280 + gap + ButtonsCardH + gap + (sim ? 160 + gap : 0) // trainer
                + sectionH + ServerCardH + gap                     // riding together
                + sectionH + StravaCardH + gap + BackupCardH + gap // data
                + sectionH + 160 + gap;                            // app
            Ui.BeginScroll(3000, view, contentH);
            float x0 = 8, y = 8;

            // ---- Profile: FTP & weight, name, power zones
            y = SettingsSection(x0, y, L.Get("section_profile"));
            var rider = new Rect(x0, y, colW, 280);
            Ui.Card(rider);
            SectionTitle(rider, L.Get("rider"), Icon.Bike);
            GUI.Label(new Rect(rider.x + 24, rider.y + 68, 300, 22), L.Get("ftp"), Theme.Caption);
            int ftp = Ui.Stepper(new Rect(rider.x + 24, rider.y + 92, rider.width - 48, 52), AppSettings.FtpW, "W", 1, 10, 50, 600);
            if (ftp != AppSettings.FtpW)
            {
                AppSettings.FtpW = ftp;
                HistoryStore.RecordFtp(ftp, "manual");
            }

            GUI.Label(new Rect(rider.x + 24, rider.y + 156, 300, 22), L.Get("weight"), Theme.Caption);
            int weight = Ui.Stepper(new Rect(rider.x + 24, rider.y + 180, rider.width - 48, 52), Mathf.RoundToInt(AppSettings.RiderWeightKg), "kg", 1, 5, 30, 200);
            if (weight != Mathf.RoundToInt(AppSettings.RiderWeightKg)) AppSettings.RiderWeightKg = weight;
            GUI.Label(new Rect(rider.x + 24, rider.yMax - 40, rider.width - 48, 24), $"{AppSettings.FtpW / Mathf.Max(1f, AppSettings.RiderWeightKg):0.00} W/kg", Theme.With(Theme.Caption, Theme.Accent));
            DrawNameCard(new Rect(x0 + colW + 24, y, colW, 280));
            y += 280 + gap;
            DrawZonesCard(new Rect(x0, y, c.width, zonesH));
            y += zonesH + gap;
            DrawHeartRateCard(new Rect(x0, y, c.width, HrCardH));
            y += HrCardH + gap;

            // ---- Trainer: difficulty, power source, virtual gears
            y = SettingsSection(x0, y, L.Get("section_trainer"));
            var trainer = new Rect(x0, y, colW, 280);
            Ui.Card(trainer);
            SectionTitle(trainer, L.Get("trainer"), Icon.Trainer);
            GUI.Label(new Rect(trainer.x + 24, trainer.y + 68, 300, 22), L.Get("difficulty"), Theme.Caption);
            GUI.Label(new Rect(trainer.xMax - 104, trainer.y + 66, 80, 26), $"{AppSettings.TrainerDifficulty * 100:0} %", Theme.With(Theme.H3, anchor: TextAnchor.MiddleRight));
            float difficulty = Ui.Slider(new Rect(trainer.x + 16, trainer.y + 96, trainer.width - 32, 44), AppSettings.TrainerDifficulty, 0f, 1f);
            difficulty = Mathf.Round(difficulty * 20) / 20;
            if (Mathf.Abs(difficulty - AppSettings.TrainerDifficulty) > 0.001f) AppSettings.TrainerDifficulty = difficulty;
            GUI.Label(new Rect(trainer.x + 24, trainer.y + 156, 300, 22), L.Get("power_source"), Theme.Caption);
            int source = Ui.Segmented(new Rect(trainer.x + 24, trainer.y + 182, trainer.width - 48, 50), new[] { L.Get("trainer"), L.Get("power_meter") }, AppSettings.PowerSource == PowerSource.Trainer ? 0 : 1);
            var newSource = source == 0 ? PowerSource.Trainer : PowerSource.PowerMeter;
            if (newSource != AppSettings.PowerSource) AppSettings.PowerSource = newSource;
            DrawControlsCard(new Rect(x0 + colW + 24, y, colW, 280));
            y += 280 + gap;
            DrawButtonsCard(new Rect(x0, y, c.width, ButtonsCardH));
            y += ButtonsCardH + gap;
            if (sim)
            {
                var simCard = new Rect(x0, y, colW, 160);
                Ui.Card(simCard);
                SectionTitle(simCard, L.Get("sim_power"), Icon.Bolt);
                GUI.Label(new Rect(simCard.xMax - 124, simCard.y + 22, 100, 26), $"{SimulatedBleBackend.RiderPowerW:0} W", Theme.With(Theme.H3, anchor: TextAnchor.MiddleRight));
                SimulatedBleBackend.RiderPowerW = Mathf.Round(Ui.Slider(new Rect(simCard.x + 16, simCard.y + 88, simCard.width - 32, 44), SimulatedBleBackend.RiderPowerW, 0f, 500f) / 5) * 5;
                y += 160 + gap;
            }

            // ---- Riding together: server, friend code
            y = SettingsSection(x0, y, L.Get("section_together"));
            DrawServerCard(new Rect(x0, y, c.width, ServerCardH));
            y += ServerCardH + gap;

            // ---- Data: Strava, backup
            y = SettingsSection(x0, y, L.Get("section_data"));
            DrawStravaCard(new Rect(x0, y, c.width, StravaCardH));
            y += StravaCardH + gap;
            DrawBackupCard(new Rect(x0, y, c.width, BackupCardH));
            y += BackupCardH + gap;

            // ---- App: language
            y = SettingsSection(x0, y, L.Get("app"));
            var app = new Rect(x0, y, colW, 160);
            Ui.Card(app);
            SectionTitle(app, L.Get("language"), Icon.Gear);
            int lang = Ui.Segmented(new Rect(app.x + 24, app.y + 80, app.width - 48, 50), new[] { "English", "Deutsch" }, AppSettings.Language == "de" ? 1 : 0);
            string newLang = lang == 1 ? "de" : "en";
            if (newLang != AppSettings.Language) AppSettings.Language = newLang;
            Ui.EndScroll(3000, view, contentH);
        }

        /// <summary>Section heading in the settings; returns where the cards start.</summary>
        private static float SettingsSection(float x, float y, string title)
        {
            GUI.Label(new Rect(x + 4, y + 8, 600, 22), title.ToUpperInvariant(), Theme.With(Theme.Overline, Theme.Accent));
            return y + 40;
        }

        /// <summary>Power zones: colour, name, range; the boundary between two zones is moved with the stepper.</summary>
        private void DrawZonesCard(Rect card)
        {
            var model = AppSettings.Zones;
            int ftp = AppSettings.FtpW;
            bool watts = model.UsesWatts;
            Ui.Card(card);
            SectionTitle(card, L.Get("zones"), Icon.Target);

            int unit = Ui.Segmented(new Rect(card.xMax - 24 - 470, card.y + 18, 230, 44), new[] { "% FTP", L.Get("watts") }, watts ? 1 : 0);
            if ((unit == 1) != watts)
            {
                model.SetUnit(unit == 1, ftp);
                AppSettings.SaveZones();
            }

            if (Ui.Button(new Rect(card.xMax - 24 - 228, card.y + 18, 228, 44), _confirmZoneReset ? L.Get("confirm_reset") : L.Get("reset_default"), _confirmZoneReset ? ButtonKind.Danger : ButtonKind.Secondary, Icon.Reset))
            {
                if (_confirmZoneReset)
                {
                    AppSettings.ResetZones();
                    _confirmZoneReset = false;
                    Ui.Toast("✓ " + L.Get("zones_reset"));
                    return;
                }

                _confirmZoneReset = true;
            }

            string u = watts ? "W" : "%";
            float y = card.y + 80;
            for (int i = 0; i < model.Zones.Count; i++)
            {
                var z = model.Zones[i];
                var row = new Rect(card.x + 24, y, card.width - 48, 50);
                var color = ColorUtility.TryParseHtmlString(z.ColorHex, out var zc) ? zc : Color.gray;
                Ui.Rect(new Rect(row.x, row.y + 6, 8, 38), color, 4);
                GUI.Label(new Rect(row.x + 22, row.y, 54, row.height), $"Z{z.Number}", Theme.With(Theme.H3, size: 18));
                GUI.Label(new Rect(row.x + 72, row.y, 260, row.height), z.Name.Get(AppSettings.Language), Theme.Body);

                double lower = z.Lower.Value;
                string range = z.Upper.HasValue ? $"{lower:0} – {z.Upper.Value.Value:0} {u}" : $"≥ {lower:0} {u}";
                string wattsHint = watts ? string.Empty : z.Upper.HasValue ? $"  ·  {z.Lower.ToWatts(ftp):0}–{z.Upper.Value.ToWatts(ftp):0} W" : $"  ·  ≥ {z.Lower.ToWatts(ftp):0} W";
                GUI.Label(new Rect(row.x + 340, row.y, 320, row.height), range + $"<color=#76828F><size=15>{wattsHint}</size></color>", Theme.With(Theme.Body, Theme.Muted));

                // Stepper for the upper bound (= lower bound of the next zone).
                if (z.Upper.HasValue)
                {
                    int small = watts ? 5 : 1, large = watts ? 25 : 5;
                    int current = Mathf.RoundToInt((float)z.Upper.Value.Value);
                    int next = Ui.Stepper(new Rect(row.xMax - 430, row.y, 430, 50), current, u, small, large, 1, watts ? 3000 : 400);
                    if (next != current)
                    {
                        model.SetBoundary(i, next);
                        AppSettings.SaveZones();
                        _confirmZoneReset = false;
                    }
                }

                y += 58;
            }
        }

        private static void SectionTitle(Rect card, string title, Icon icon)
        {
            Ui.DrawIcon(new Rect(card.x + 24, card.y + 22, 24, 24), icon, Theme.Accent);
            GUI.Label(new Rect(card.x + 58, card.y + 18, card.width - 80, 32), title, Theme.H2);
        }

        // ------------------------------------------------------------------ ride

        // Top of the cards at the right / left side of the ride screen (set by DrawRide).
        private float _sideTop = 24, _leftTop = 100;

        private void DrawRide()
        {
            var live = _ble.Hub.Live;
            var pos = _ride.Position;
            var state = _ride.WorkoutState;
            int ftp = AppSettings.FtpW;
            bool workout = _ride.Engine != null && state != null;

            // Top left: pause / end
            if (Ui.IconButton(new Rect(24, 24, 60, 60), _ride.IsPaused ? Icon.Play : Icon.Pause)) _ride.TogglePause();
            if (Ui.IconButton(new Rect(92, 24, 60, 60), Icon.Stop)) _confirmEnd = true;

            // Metrics bar (top centre). The side cards (320 wide + margins) go next to it when there is room,
            // otherwise below the distance/grade chips.
            const float sideSpace = 344 + 16;
            bool sidesBeside = _w - (2 * sideSpace) >= 640;
            float barW = sidesBeside ? Mathf.Min(900, _w - (2 * sideSpace)) : Mathf.Min(900, _w - 48);
            var bar = new Rect((_w - barW) / 2f, 20, barW, 112);
            Ui.Card(bar, Theme.Alpha(Theme.Background, 0.88f));
            float powerW = barW * 0.24f;
            float side = (barW - powerW) / 6f;

            // Left: the ride so far (time, distance, elevation gained); right: live values (speed, cadence, heart rate).
            // All six share one fixed font size: the largest at which the widest value of each field fits (it only
            // depends on the screen width, so it does not change while riding).
            double km = _ride.RideDistanceM / 1000;
            var metrics = new (Icon Icon, string Value, string Unit, Color Color)[]
            {
                (Icon.Timer, Ui.Clock(_ride.ElapsedS), null, Theme.Text),
                (Icon.Distance, km < 10 ? $"{km:0.00}" : $"{km:0.0}", "km", Theme.Info),
                (Icon.Ascent, $"{_ride.AscentM:0}", "m", Theme.Success),
                (Icon.Speed, $"{_ride.SpeedMps * 3.6:0.0}", "km/h", Theme.Text),
                (Icon.Cadence, live.CadenceRpm.HasValue ? $"{live.CadenceRpm:0}" : "–", "rpm", Theme.Text),
                (Icon.Heart, live.HeartRateBpm.HasValue ? $"{live.HeartRateBpm}" : "–", "bpm", Theme.HeartRed),
            };
            var metricStyle = Theme.With(Theme.Value, anchor: TextAnchor.MiddleCenter, size: MetricFontSize(side));

            for (int i = 0; i < 3; i++) Metric(new Rect(bar.x + (i * side), bar.y, side, bar.height), metrics[i].Icon, metrics[i].Value, metrics[i].Unit, metrics[i].Color, metricStyle);

            // Power (centre), coloured by how well the target is held.
            var p = new Rect(bar.x + (3 * side), bar.y - 8, powerW, bar.height + 16);
            Ui.Card(p, Theme.Surface2);
            Color powerColor = Theme.Text;
            if (workout && state.TargetW.HasValue && state.TargetW.Value > 0)
            {
                double ratio = _ride.Power3s / state.TargetW.Value;
                powerColor = ratio < 0.94 ? Theme.Warning : ratio > 1.06 ? Theme.Danger : Theme.Success;
            }

            GUI.Label(new Rect(p.x, p.y + 10, p.width, 18), L.Get("power").ToUpperInvariant(), Theme.With(Theme.Overline, anchor: TextAnchor.MiddleCenter));
            GUI.Label(new Rect(p.x, p.y + 26, p.width, 70), $"{_ride.Power3s:0}<size=24><color=#B3BECA> W</color></size>", Theme.With(Theme.Display, powerColor, 60));
            string sub = workout && state.TargetW.HasValue ? $"{L.Get("target")} {state.TargetW} W" : $"{_ride.Power3s / Mathf.Max(1f, AppSettings.RiderWeightKg):0.0} W/kg";
            GUI.Label(new Rect(p.x, p.yMax - 32, p.width, 22), sub, Theme.With(Theme.Caption, anchor: TextAnchor.MiddleCenter));

            for (int i = 3; i < 6; i++) Metric(new Rect(p.xMax + ((i - 3) * side), bar.y, side, bar.height), metrics[i].Icon, metrics[i].Value, metrics[i].Unit, metrics[i].Color, metricStyle);

            // Chips under the bar: gradient (and braking)
            float cy = bar.yMax + 16;
            _sideTop = sidesBeside ? 24 : cy + 54;
            _leftTop = sidesBeside ? 100 : cy + 54;
            string grade = $"{pos.GradePercent:+0.0;-0.0;0.0} %";
            var gradeColor = pos.GradePercent > 4 ? Theme.Danger : pos.GradePercent > 1.5 ? Theme.Warning : pos.GradePercent < -1.5 ? Theme.Info : Theme.Muted;
            var chipStyle = Theme.With(Theme.H3, size: 17, anchor: TextAnchor.MiddleLeft);
            // The gradient only on climbs from 3 % (not on flat roads or descents).
            bool showGrade = pos.GradePercent >= 3;
            float w2 = showGrade ? chipStyle.CalcSize(new GUIContent(grade)).x + 56 : 0;
            string brake = L.Get("braking");
            float w3 = _ride.IsBraking ? chipStyle.CalcSize(new GUIContent(brake)).x + 56 : 0;
            float cx = (_w - (w2 + w3 + (w2 > 0 && w3 > 0 ? 10 : 0))) / 2f;
            if (showGrade) InfoChip(new Rect(cx, cy, w2, 38), Icon.GradeUp, grade, gradeColor, chipStyle);
            if (_ride.IsBraking) InfoChip(new Rect(cx + w2 + (w2 > 0 ? 10 : 0), cy, w3, 38), Icon.Hand, brake, Theme.Warning, chipStyle);

            // Warnings
            string warning = _ble.Hub.TrainerAddress == null ? L.Get("no_trainer")
                : _ride.IsAutoPaused ? L.Get("paused_pedal")
                : _ble.Hub.LowCadenceProtectionActive ? L.Get("low_cadence")
                : _ble.Hub.ErgSpeedTooHigh ? (_ble.Hub.ErgMaxSpeedKmh is double max && max >= 5
                    ? string.Format(L.Get("erg_too_fast_kmh"), max)
                    : L.Get("erg_too_fast"))
                : _ride.IsPaused ? L.Get("paused")
                : null;
            if (warning != null)
            {
                var style = Theme.With(Theme.Body, Theme.Background, 18, TextAnchor.MiddleCenter);
                float ww = style.CalcSize(new GUIContent(warning)).x + 48;
                var wr = new Rect((_w - ww) / 2f, cy + 48, ww, 44);
                Ui.Rect(wr, Theme.Warning, 22);
                GUI.Label(wr, warning, style);
            }

            if (workout) DrawWorkoutPanel();
            else DrawRideStrip();
            if (!_ride.IsGroupRide) DrawJunctionCard(workout);
            DrawClimbTimer(workout);
            if (_ride.GearsActive) DrawGearControl();
            if (_ride.IsGroupRide && _group != null) DrawGroupOverlay();

            // Cue
            if (!string.IsNullOrEmpty(_ride.CueText) && _ride.ClockS < _ride.CueUntil)
            {
                float a = Mathf.Clamp01((_ride.CueUntil - _ride.ClockS) / 1.5f);
                var old = GUI.color;
                GUI.color = new Color(1, 1, 1, a);
                var style = Theme.With(Theme.H1, size: 40, anchor: TextAnchor.MiddleCenter);
                float cw = Mathf.Min(_w - 200, style.CalcSize(new GUIContent(_ride.CueText)).x + 80);
                var r = new Rect((_w - cw) / 2f, (_h / 2f) - 50, cw, 100);
                Ui.Card(r, Theme.Alpha(Theme.Background, 0.85f), 28);
                GUI.Label(r, _ride.CueText, style);
                GUI.color = old;
            }

            if (_confirmEnd) DrawEndConfirm();
        }

        /// <summary>Metric in the ride bar: icon above, value with unit (no text label, so it fits in every language).</summary>
        /// <summary>Widest value of each field in the ride bar (the font is sized for these).</summary>
        private static readonly (string Value, string Unit)[] WidestMetrics =
        {
            ("8:88:88", null), ("188.8", "km"), ("8888", "m"), ("88.8", "km/h"), ("188", "rpm"), ("188", "bpm"),
        };

        private float _metricFontWidth = -1;
        private int _metricFontSize;

        private int MetricFontSize(float width)
        {
            if (Mathf.Abs(width - _metricFontWidth) < 0.5f) return _metricFontSize;
            var style = Theme.With(Theme.Value, anchor: TextAnchor.MiddleCenter);
            foreach (var (value, unit) in WidestMetrics)
            {
                while (style.fontSize > 18 && style.CalcSize(new GUIContent(MetricText(value, unit))).x > width - 8) style.fontSize -= 1;
            }

            _metricFontWidth = width;
            _metricFontSize = style.fontSize;
            return _metricFontSize;
        }

        private static string MetricText(string value, string unit) => unit != null ? $"{value}<size=15><color=#B3BECA> {unit}</color></size>" : value;

        /// <summary>Metric in the ride bar: icon above, value with unit (no text label, so it fits in every language).</summary>
        private static void Metric(Rect r, Icon icon, string value, string unit, Color iconColor, GUIStyle style)
        {
            Ui.DrawIcon(new Rect(r.center.x - 10, r.y + 16, 20, 20), icon, iconColor);
            GUI.Label(new Rect(r.x, r.y + 40, r.width, 56), MetricText(value, unit), style);
        }

        private static void InfoChip(Rect r, Icon icon, string text, Color iconColor, GUIStyle style)
        {
            Ui.Card(r, Theme.Alpha(Theme.Background, 0.85f), r.height / 2f);
            Ui.DrawIcon(new Rect(r.x + 14, r.center.y - 9, 18, 18), icon, iconColor);
            GUI.Label(new Rect(r.x + 40, r.y, r.width - 48, r.height), text, style);
        }

        private void DrawEndConfirm()
        {
            Ui.Rect(_full, new Color(0, 0, 0, 0.5f), 0);
            var r = new Rect((_w - 520) / 2f, (_h - 230) / 2f, 520, 230);
            Ui.Card(r, Theme.Surface, 24);
            GUI.Label(new Rect(r.x + 32, r.y + 28, r.width - 64, 40), L.Get("end_ride_q"), Theme.H2);
            GUI.Label(new Rect(r.x + 32, r.y + 70, r.width - 64, 50), L.Get("end_ride_hint"), Theme.BodyMuted);
            if (Ui.Button(new Rect(r.x + 32, r.yMax - 84, 216, 56), L.Get("continue_riding"), ButtonKind.Secondary)) _confirmEnd = false;
            if (Ui.Button(new Rect(r.xMax - 248, r.yMax - 84, 216, 56), L.Get("end_ride"), ButtonKind.Primary, Icon.Stop))
            {
                _confirmEnd = false;
                _ride.EndRide();
            }
        }

        private void DrawWorkoutPanel()
        {
            var engine = _ride.Engine;
            var state = _ride.WorkoutState;
            int ftp = engine.FtpW;

            // Interval card (right)
            var card = new Rect(_w - 344, _sideTop, 320, 330);
            if (_ride.WorkoutComplete)
            {
                // Done: the ride continues as a free ride (trainer simulates the road).
                card.height = 150;
                Ui.Card(card, Theme.Alpha(Theme.Background, 0.88f));
                GUI.Label(new Rect(card.x + 22, card.y + 18, card.width - 44, 18), L.Get("finished").ToUpperInvariant(), Theme.With(Theme.Overline, Theme.Success));
                GUI.Label(new Rect(card.x + 22, card.y + 42, card.width - 44, 48), L.Get("free_ride_on"), Theme.With(Theme.H1, size: 30));
                GUI.Label(new Rect(card.x + 22, card.y + 92, card.width - 44, 44), $"Workout {Ui.Clock(engine.TotalDurationS)} · {L.Get("time")} {Ui.Clock(_ride.ElapsedS)}", Theme.Caption);
            }
            else
            {
                Ui.Card(card, Theme.Alpha(Theme.Background, 0.88f));
            }

            if (state.Segment != null && !_ride.WorkoutComplete)
            {
                var seg = state.Segment;
                string kind = seg.Repeat.HasValue ? $"{L.Get("interval")} {seg.Repeat}" : L.Get("block");
                GUI.Label(new Rect(card.x + 22, card.y + 18, card.width - 44, 18), kind.ToUpperInvariant(), Theme.Overline);
                if (state.TargetW.HasValue)
                {
                    var zc = ZoneColor(state.TargetW.Value, ftp);
                    Ui.Rect(new Rect(card.x + 22, card.y + 46, 8, 40), zc, 4);
                    GUI.Label(new Rect(card.x + 40, card.y + 42, card.width - 60, 48), $"{state.TargetW} W <size=18><color=#B3BECA>{state.TargetW.Value * 100.0 / ftp:0} %</color></size>", Theme.With(Theme.H1, size: 34));
                }
                else
                {
                    GUI.Label(new Rect(card.x + 22, card.y + 42, card.width - 44, 48), L.Get("free"), Theme.With(Theme.H1, size: 30));
                }

                GUI.Label(new Rect(card.x, card.y + 92, card.width, 76), Ui.Clock(state.SegmentRemainingS), Theme.With(Theme.Display, size: 64));
                float progress = seg.DurationS > 0 ? Mathf.Clamp01((float)(1 - (state.SegmentRemainingS / seg.DurationS))) : 0;
                var pb = new Rect(card.x + 22, card.y + 172, card.width - 44, 8);
                Ui.Rect(pb, Theme.Surface3, 4);
                Ui.Rect(new Rect(pb.x, pb.y, pb.width * progress, pb.height), Theme.Accent, 4);

                float y = card.y + 192;
                if (state.Cadence.HasValue)
                {
                    GUI.Label(new Rect(card.x + 22, y, card.width - 44, 22), $"{L.Get("cadence")}: {state.Cadence}", Theme.Caption);
                    y += 24;
                }

                if (state.NextSegment != null)
                {
                    var next = state.NextSegment;
                    double? nextW = next.BaseTargetW(0, ftp);
                    string nextText = nextW.HasValue ? $"{nextW.Value * engine.GlobalIntensity:0} W" : L.Get("free");
                    GUI.Label(new Rect(card.x + 22, y, card.width - 44, 22), $"{L.Get("next")}: {Ui.Clock(next.DurationS)} @ {nextText}", Theme.Caption);
                }

                // Intensity and skip
                var row = new Rect(card.x + 22, card.yMax - 70, card.width - 44, 50);
                if (Ui.IconButton(new Rect(row.x, row.y, 50, 50), Icon.ChevronLeft)) _ride.AdjustBlockIntensity(-0.02);
                GUI.Label(new Rect(row.x + 50, row.y, 110, 50), $"{state.SegmentIntensity * 100:0} %", Theme.With(Theme.H3, anchor: TextAnchor.MiddleCenter));
                if (Ui.IconButton(new Rect(row.x + 160, row.y, 50, 50), Icon.ChevronRight)) _ride.AdjustBlockIntensity(0.02);
                if (Ui.IconButton(new Rect(row.xMax - 50, row.y, 50, 50), Icon.Skip, ButtonKind.Ghost)) _ride.SkipBlock();
            }

            // Workout profile (bottom)
            var graph = new Rect(24, _h - 136, _w - 48, 112);
            Ui.Card(graph, Theme.Alpha(Theme.Background, 0.85f));
            WorkoutProfile(new Rect(graph.x + 16, graph.y + 30, graph.width - 32, graph.height - 42), engine.Workout, false, engine.ElapsedS);
            GUI.Label(new Rect(graph.x + 18, graph.y + 6, 300, 22), $"{Ui.Clock(engine.ElapsedS)} / {Ui.Clock(engine.TotalDurationS)}", Theme.Caption);
        }

        // ------------------------------------------------------------------ summary

        private void DrawSummary()
        {
            var s = _ride.Summary;
            Ui.Rect(_full, new Color(0, 0, 0, 0.45f), 0);
            float cw = Mathf.Min(900, _w - 80);
            const int tileCount = 6, cols = 3; // always two full rows
            float contentH = 108 + (((tileCount + cols - 1) / cols) * 92) + 8 + (s.PowerCurve != null && s.PowerCurve.Count > 0 ? 146 : 0) + (s.EstimatedFtpW.HasValue ? 88 : 0) + SummaryClimbsH(s) + 104;
            float ch = Mathf.Min(_h - 56, contentH);
            var r = new Rect((_w - cw) / 2f, (_h - ch) / 2f, cw, ch);
            Ui.Card(r, Theme.Surface, 24);
            float x = r.x + 32, w = r.width - 64;

            Ui.Rect(new Rect(x, r.y + 30, 52, 52), Theme.Alpha(Theme.Success, 0.18f), 26);
            Ui.DrawIcon(new Rect(x + 13, r.y + 43, 26, 26), Icon.Check, Theme.Success);
            GUI.Label(new Rect(x + 68, r.y + 26, w - 68, 36), L.Get("ride_complete"), Theme.H1);
            GUI.Label(new Rect(x + 68, r.y + 62, w - 68, 24), s.Title, Theme.BodyMuted);

            // Stat tiles: the ride (time, distance, elevation), then the effort (power, calories, heart rate).
            var tiles = new List<(string Label, string Value)>
            {
                (L.Get("time"), Ui.Clock(s.DurationS)),
                (L.Get("distance"), $"{s.DistanceM / 1000:0.00} km"),
                (L.Get("ascent"), $"{s.AscentM:0} m"),
                (L.Get("avg_power"), $"{s.AvgPowerW:0} W"),
                (L.Get("calories"), $"{s.Kcal:0} kcal"),
                s.AvgHeartRate.HasValue
                    ? (L.Get("avg_hr"), $"{s.AvgHeartRate:0} bpm")
                    : (L.Get("max_power"), $"{s.MaxPowerW ?? 0} W"),
            };
            float tw = (w - (12 * (cols - 1))) / cols;
            for (int i = 0; i < tiles.Count; i++)
            {
                var t = new Rect(x + ((i % cols) * (tw + 12)), r.y + 108 + ((i / cols) * 92), tw, 80);
                Ui.Rect(t, Theme.Surface2, Theme.RadiusSmall);
                GUI.Label(new Rect(t.x + 16, t.y + 12, t.width - 32, 18), tiles[i].Label.ToUpperInvariant(), Theme.Overline);
                GUI.Label(new Rect(t.x + 16, t.y + 32, t.width - 32, 40), tiles[i].Value, Theme.With(Theme.Value, size: 28));
            }

            float y = r.y + 108 + (((tiles.Count + cols - 1) / cols) * 92) + 8;

            // Power curve as bars
            if (s.PowerCurve != null && s.PowerCurve.Count > 0)
            {
                GUI.Label(new Rect(x, y, 300, 18), L.Get("power_curve").ToUpperInvariant(), Theme.Overline);
                var curve = s.PowerCurve.ToList();
                double max = curve.Max(kv => kv.Value);
                float bw = (w - (10 * (curve.Count - 1))) / curve.Count; // bars share the full width
                for (int i = 0; i < curve.Count; i++)
                {
                    float h = 14 + (float)(curve[i].Value / max) * 50;
                    var b = new Rect(x + (i * (bw + 10)), y + 116 - h, bw, h);
                    Ui.Rect(b, ZoneColor(curve[i].Value, AppSettings.FtpW), 6);
                    GUI.Label(new Rect(b.x, b.y - 22, bw, 20), $"{curve[i].Value:0}", Theme.With(Theme.Caption, Theme.Text, 14, TextAnchor.MiddleCenter));
                    GUI.Label(new Rect(b.x, y + 118, bw, 20), FormatDuration(curve[i].Key), Theme.With(Theme.Caption, size: 13, anchor: TextAnchor.MiddleCenter));
                }

                y += 146;
            }

            y = DrawSummaryClimbs(s, x, y, w);

            // FTP test result
            if (s.EstimatedFtpW.HasValue)
            {
                var f = new Rect(x, y, w, 72);
                Ui.Rect(f, Theme.Alpha(Theme.Accent, 0.14f), Theme.RadiusSmall);
                Ui.DrawIcon(new Rect(f.x + 18, f.center.y - 14, 28, 28), Icon.Target, Theme.Accent);
                GUI.Label(new Rect(f.x + 60, f.y, w - 300, f.height), $"{L.Get("new_ftp")}: <b>{s.EstimatedFtpW} W</b>  <color=#B3BECA>({AppSettings.FtpW} W)</color>", Theme.Body);
                if (s.EstimatedFtpW != AppSettings.FtpW && Ui.Button(new Rect(f.xMax - 230, f.y + 12, 214, 48), L.Get("apply_ftp"), ButtonKind.Primary, Icon.Check))
                {
                    AppSettings.FtpW = s.EstimatedFtpW.Value;
                    HistoryStore.RecordFtp(s.EstimatedFtpW.Value, "test");
                    Ui.Toast("✓ FTP " + AppSettings.FtpW + " W");
                }
            }

            // Actions
            var actions = new Rect(x, r.yMax - 88, w, 56);
            if (!string.IsNullOrEmpty(s.FitPath))
            {
                float w1 = Ui.ButtonWidth(L.Get("share"), true, 56), w2 = Ui.ButtonWidth(L.Get("save_downloads"), true, 56);
                if (Ui.Button(new Rect(actions.x, actions.y, w1, 56), L.Get("share"), ButtonKind.Secondary, Icon.Share)) Ui.Toast(FileActions.Share(s.FitPath, L.Get("share")));
                if (Ui.Button(new Rect(actions.x + w1 + 12, actions.y, w2, 56), L.Get("save_downloads"), ButtonKind.Secondary, Icon.Download)) Ui.Toast(FileActions.SaveToDownloads(s.FitPath));
                float w3 = Ui.ButtonWidth(L.Get("strava_uploading"), true, 56);
                DrawSummaryStravaButton(new Rect(actions.x + w1 + w2 + 24, actions.y, w3, 56), s.FitPath, s.Title);
            }

            if (Ui.Button(new Rect(actions.xMax - 180, actions.y, 180, 56), L.Get("done"), ButtonKind.Primary))
            {
                _ride.Close();
                Navigate(Page.Home);
            }
        }

        private static string FormatDuration(int seconds) => seconds < 60 ? $"{seconds}s" : seconds < 3600 ? $"{seconds / 60}m" : $"{seconds / 3600}h";

        private static string FormatTime(double seconds) => Ui.Clock(seconds);
    }
}

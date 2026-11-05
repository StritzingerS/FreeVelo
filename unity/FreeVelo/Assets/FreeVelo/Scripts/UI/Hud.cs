using System.Linq;
using FreeVelo.App;
using FreeVelo.Ble;
using FreeVelo.Core.Training;
using FreeVelo.Core.Workouts;
using FreeVelo.Devices.Session;
using FreeVelo.Ride;
using UnityEngine;

namespace FreeVelo.UI
{
    /// <summary>
    /// First-version user interface (IMGUI, built in code). Laid out on a virtual 1280×720 canvas that is scaled to the screen.
    /// </summary>
    public sealed partial class Hud : MonoBehaviour
    {
        private enum Page
        {
            Home,
            Devices,
            Workouts,
            FtpTests,
            Settings,
            Editor,
        }

        private const float RefW = 1280f;
        private const float RefH = 720f;

        private BleService _ble;
        private RideController _ride;
        private Page _page = Page.Home;
        private float _w;
        private float _h;
        private bool _stylesReady;
        private GUIStyle _title;
        private GUIStyle _label;
        private GUIStyle _small;
        private GUIStyle _tileLabel;
        private GUIStyle _tileValue;
        private GUIStyle _button;
        private GUIStyle _cue;
        private GUIStyle _panel;
        private GUIStyle _textField;
        private GUIStyle _paletteLabel;
        private GUIStyle _tinyCenter;
        private GUIStyle _labelCenter;
        private readonly PowerZoneModel _zones = PowerZoneModel.Coggan();
        private string _fileMessage;

        public void Initialize(BleService ble, RideController ride)
        {
            _ble = ble;
            _ride = ride;
        }

        private void OnGUI()
        {
            if (_ble == null || _ride == null) return;
            EnsureStyles();

            float scale = Mathf.Min(Screen.width / RefW, Screen.height / RefH);
            _w = Screen.width / scale;
            _h = Screen.height / scale;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1));

            switch (_ride.State)
            {
                case RideState.Riding:
                    DrawRide();
                    return;
                case RideState.Finished:
                    DrawSummary();
                    return;
            }

            switch (_page)
            {
                case Page.Home: DrawHome(); break;
                case Page.Devices: DrawDevices(); break;
                case Page.Workouts: DrawWorkoutLibrary(); break;
                case Page.FtpTests: DrawWorkouts(new[] { FtpTests.Create(FtpTestKind.Ramp), FtpTests.Create(FtpTestKind.TwentyMinute), FtpTests.Create(FtpTestKind.TwoByEight) }, L.Get("ftp_tests")); break;
                case Page.Settings: DrawSettings(); break;
                case Page.Editor:
                    if (_editor != null) DrawEditor();
                    else _page = Page.Workouts;
                    break;
            }
        }

        // ------------------------------------------------------------------ pages

        private void DrawHome()
        {
            var area = new Rect(30, 30, 420, _h - 60);
            GUI.Box(area, GUIContent.none, _panel);
            GUILayout.BeginArea(new Rect(area.x + 20, area.y + 15, area.width - 40, area.height - 30));
            GUILayout.Label("FreeVelo", _title);
            GUILayout.Label(DeviceStatus(), _small);
            GUILayout.Space(10);
            if (GUILayout.Button(L.Get("free_ride"), _button, GUILayout.Height(70))) _ride.StartRide(null);
            if (GUILayout.Button(L.Get("workouts"), _button, GUILayout.Height(70))) _page = Page.Workouts;
            if (GUILayout.Button(L.Get("ftp_tests"), _button, GUILayout.Height(70))) _page = Page.FtpTests;
            if (GUILayout.Button(L.Get("devices"), _button, GUILayout.Height(70))) _page = Page.Devices;
            if (GUILayout.Button(L.Get("settings"), _button, GUILayout.Height(70))) _page = Page.Settings;
            GUILayout.EndArea();
        }

        private string DeviceStatus()
        {
            var hub = _ble.Hub;
            string trainer = hub.TrainerAddress != null ? $"{_ble.NameOf(hub.TrainerAddress)} ({hub.TrainerProtocol})" : L.Get("none");
            string pm = hub.PowerMeterAddress != null ? _ble.NameOf(hub.PowerMeterAddress) : L.Get("none");
            string hr = hub.HeartRateAddress != null ? _ble.NameOf(hub.HeartRateAddress) : L.Get("none");
            return $"{L.Get("trainer")}: {trainer}\n{L.Get("power_meter")}: {pm}\n{L.Get("heart_rate")}: {hr}\nFTP {AppSettings.FtpW} W · {AppSettings.RiderWeightKg:0} kg";
        }

        private void DrawDevices()
        {
            var area = new Rect(30, 30, _w - 60, _h - 60);
            GUI.Box(area, GUIContent.none, _panel);
            GUILayout.BeginArea(new Rect(area.x + 20, area.y + 15, area.width - 40, area.height - 30));
            GUILayout.BeginHorizontal();
            GUILayout.Label(L.Get("devices") + $"  ({_ble.BackendName})", _title);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(_ble.IsScanning ? L.Get("stop_scan") : L.Get("scan"), _button, GUILayout.Width(180), GUILayout.Height(60)))
            {
                if (_ble.IsScanning) _ble.StopScan();
                else _ble.StartScan();
            }

            if (GUILayout.Button(L.Get("back"), _button, GUILayout.Width(180), GUILayout.Height(60))) _page = Page.Home;
            GUILayout.EndHorizontal();

            GUILayout.Label(DeviceStatus(), _small);
            GUILayout.Space(8);

            var devices = _ble.Devices.Take(7).ToList();
            if (devices.Count == 0) GUILayout.Label(L.Get("no_devices"), _label);
            foreach (var d in devices)
            {
                GUILayout.BeginHorizontal(GUILayout.Height(58));
                string name = string.IsNullOrEmpty(d.Name) ? d.Address : d.Name;
                GUILayout.Label($"{name}   <size=18>{d.Tags}   {d.Rssi} dBm   {d.State ?? string.Empty}</size>", _label, GUILayout.ExpandWidth(true));
                if (GUILayout.Button(d.IsConnected ? L.Get("disconnect") : L.Get("connect"), _button, GUILayout.Width(220), GUILayout.Height(52)))
                {
                    if (d.IsConnected) _ble.Disconnect(d.Address);
                    else _ble.Connect(d.Address);
                }

                GUILayout.EndHorizontal();
            }

            GUILayout.FlexibleSpace();
            foreach (var line in _ble.Log.Take(4)) GUILayout.Label(line, _small);
            if (!string.IsNullOrEmpty(_ble.LogFilePath)) GUILayout.Label("Log: " + _ble.LogFilePath, _small);
            GUILayout.EndArea();
        }

        private void DrawWorkouts(Workout[] workouts, string title)
        {
            var area = new Rect(30, 30, _w - 60, _h - 60);
            GUI.Box(area, GUIContent.none, _panel);
            GUILayout.BeginArea(new Rect(area.x + 20, area.y + 15, area.width - 40, area.height - 30));
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{title}   <size=20>FTP {AppSettings.FtpW} W</size>", _title);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(L.Get("back"), _button, GUILayout.Width(180), GUILayout.Height(60))) _page = Page.Home;
            GUILayout.EndHorizontal();
            GUILayout.Space(10);

            const int columns = 2;
            for (int i = 0; i < workouts.Length; i += columns)
            {
                GUILayout.BeginHorizontal();
                for (int c = 0; c < columns; c++)
                {
                    if (i + c >= workouts.Length)
                    {
                        GUILayout.FlexibleSpace();
                        continue;
                    }

                    var w = workouts[i + c];
                    string text = $"{w.Name.Get(AppSettings.Language)}\n<size=17>{w.TotalDurationS / 60} {L.Get("min")} · {w.Category}</size>";
                    if (GUILayout.Button(text, _button, GUILayout.Height(workouts.Length > 6 ? 76 : 110)))
                    {
                        _ride.StartRide(w);
                    }
                }

                GUILayout.EndHorizontal();
            }

            if (workouts.Length <= 3)
            {
                GUILayout.Space(10);
                foreach (var w in workouts) GUILayout.Label($"<b>{w.Name.Get(AppSettings.Language)}</b>: {w.Description?.Get(AppSettings.Language)}", _small);
            }

            GUILayout.EndArea();
        }

        // ------------------------------------------------------------------ workout library

        private bool _ownTab;
        private int _libraryPage;
        private string _confirmDelete;

        private void DrawWorkoutLibrary()
        {
            var area = new Rect(30, 30, _w - 60, _h - 60);
            GUI.Box(area, GUIContent.none, _panel);
            GUILayout.BeginArea(new Rect(area.x + 20, area.y + 15, area.width - 40, area.height - 30));
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{L.Get("workouts")}   <size=20>FTP {AppSettings.FtpW} W</size>", _title);
            GUILayout.FlexibleSpace();
            if (GUILayout.Toggle(!_ownTab, L.Get("templates"), _button, GUILayout.Width(200), GUILayout.Height(60)) && _ownTab) { _ownTab = false; _libraryPage = 0; }
            if (GUILayout.Toggle(_ownTab, L.Get("my_workouts"), _button, GUILayout.Width(240), GUILayout.Height(60)) && !_ownTab) { _ownTab = true; _libraryPage = 0; }
            if (GUILayout.Button(L.Get("back"), _button, GUILayout.Width(160), GUILayout.Height(60)))
            {
                _page = Page.Home;
                WorkoutStore.LastMessage = null;
            }

            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            if (_ownTab) DrawOwnWorkouts();
            else DrawTemplates();

            if (!string.IsNullOrEmpty(WorkoutStore.LastMessage))
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label(WorkoutStore.LastMessage, _small);
            }

            GUILayout.EndArea();
        }

        /// <summary>Built-in workouts (in the user's edited version where there is one): start, edit, reset.</summary>
        private void DrawTemplates()
        {
            var library = WorkoutStore.Library;
            var templates = library.Templates;
            const int perPage = 7;
            int pages = Mathf.Max(1, Mathf.CeilToInt(templates.Count / (float)perPage));
            _libraryPage = Mathf.Clamp(_libraryPage, 0, pages - 1);
            foreach (var w in templates.Skip(_libraryPage * perPage).Take(perPage).ToList())
            {
                bool modified = library.IsModified(w.Id);
                GUILayout.BeginHorizontal(GUILayout.Height(56));
                string mark = modified ? $"  <size=16><color=#ffd34d>✎ {L.Get("modified")}</color></size>" : string.Empty;
                GUILayout.Label($"{w.Name.Get(AppSettings.Language)}   <size=17>{w.TotalDurationS / 60} {L.Get("min")} · {w.Category}</size>{mark}", _label, GUILayout.ExpandWidth(true));
                if (GUILayout.Button("▶ " + L.Get("start"), _button, GUILayout.Width(150), GUILayout.Height(50))) _ride.StartRide(w);
                if (GUILayout.Button("✎ " + L.Get("edit"), _button, GUILayout.Width(150), GUILayout.Height(50))) OpenEditor(w, true);
                if (modified)
                {
                    bool confirming = _confirmReset == w.Id;
                    if (GUILayout.Button(confirming ? L.Get("confirm_reset") : L.Get("reset_default"), _button, GUILayout.Width(confirming ? 230 : 190), GUILayout.Height(50)))
                    {
                        if (confirming)
                        {
                            library.ResetToDefault(w.Id);
                            WorkoutStore.LastMessage = "✓ " + L.Get("reset_done") + ": " + w.Name.Get(AppSettings.Language);
                            _confirmReset = null;
                        }
                        else
                        {
                            _confirmReset = w.Id;
                        }
                    }
                }
                else
                {
                    GUILayout.Space(194);
                }

                GUILayout.EndHorizontal();
            }

            if (pages > 1)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("◀", _button, GUILayout.Width(80), GUILayout.Height(50))) _libraryPage--;
                GUILayout.Label($"{L.Get("page")} {_libraryPage + 1}/{pages}", _label, GUILayout.Width(160));
                if (GUILayout.Button("▶", _button, GUILayout.Width(80), GUILayout.Height(50))) _libraryPage++;
                GUILayout.EndHorizontal();
            }
        }

        private string _confirmReset;

        private void DrawOwnWorkouts()
        {
            var library = WorkoutStore.Library;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("＋ " + L.Get("new_workout"), _button, GUILayout.Height(56))) OpenEditor(null, false);
            if (GUILayout.Button("⤓ " + L.Get("import") + " (.zwo / .erg / .mrc)", _button, GUILayout.Height(56))) WorkoutStore.PickFiles();
            GUILayout.EndHorizontal();
            GUILayout.Space(6);

            var workouts = library.Workouts;
            if (workouts.Count == 0)
            {
                GUILayout.Label(L.Get("no_own_workouts"), _label);
            }

            const int perPage = 6;
            int pages = Mathf.Max(1, Mathf.CeilToInt(workouts.Count / (float)perPage));
            _libraryPage = Mathf.Clamp(_libraryPage, 0, pages - 1);
            foreach (var w in workouts.Skip(_libraryPage * perPage).Take(perPage).ToList())
            {
                GUILayout.BeginHorizontal(GUILayout.Height(58));
                GUILayout.Label($"{w.Name.Get(AppSettings.Language)}   <size=17>{w.TotalDurationS / 60} {L.Get("min")}</size>", _label, GUILayout.ExpandWidth(true));
                if (GUILayout.Button("▶ " + L.Get("start"), _button, GUILayout.Width(150), GUILayout.Height(52))) _ride.StartRide(w);
                if (GUILayout.Button("✎ " + L.Get("edit"), _button, GUILayout.Width(150), GUILayout.Height(52))) OpenEditor(w, false);
                if (GUILayout.Button(L.Get("share_workout"), _button, GUILayout.Width(150), GUILayout.Height(52))) WorkoutStore.Share(w);
                bool confirming = _confirmDelete == w.Id;
                if (GUILayout.Button(confirming ? L.Get("confirm_delete") : L.Get("delete"), _button, GUILayout.Width(confirming ? 230 : 150), GUILayout.Height(52)))
                {
                    if (confirming)
                    {
                        WorkoutStore.Delete(w);
                        _confirmDelete = null;
                    }
                    else
                    {
                        _confirmDelete = w.Id;
                    }
                }

                GUILayout.EndHorizontal();
            }

            if (pages > 1)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("◀", _button, GUILayout.Width(80), GUILayout.Height(50))) _libraryPage--;
                GUILayout.Label($"{L.Get("page")} {_libraryPage + 1}/{pages}", _label, GUILayout.Width(160));
                if (GUILayout.Button("▶", _button, GUILayout.Width(80), GUILayout.Height(50))) _libraryPage++;
                GUILayout.EndHorizontal();
            }

            foreach (var error in library.Errors.Take(3)) GUILayout.Label("✗ " + error, _small);
        }

        private void DrawSettings()
        {
            var area = new Rect(30, 30, _w - 60, _h - 60);
            GUI.Box(area, GUIContent.none, _panel);
            GUILayout.BeginArea(new Rect(area.x + 20, area.y + 15, area.width - 40, area.height - 30));
            GUILayout.BeginHorizontal();
            GUILayout.Label(L.Get("settings"), _title);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(L.Get("back"), _button, GUILayout.Width(180), GUILayout.Height(60)))
            {
                _ble.ApplySettings();
                _page = Page.Home;
            }

            GUILayout.EndHorizontal();
            GUILayout.Space(10);

            int ftp = Stepper(L.Get("ftp"), AppSettings.FtpW, "W", 1, 10);
            if (ftp != AppSettings.FtpW) AppSettings.FtpW = ftp;

            int weight = Stepper(L.Get("weight"), Mathf.RoundToInt(AppSettings.RiderWeightKg), "kg", 1, 5);
            if (weight != Mathf.RoundToInt(AppSettings.RiderWeightKg)) AppSettings.RiderWeightKg = weight;

            GUILayout.BeginHorizontal(GUILayout.Height(60));
            GUILayout.Label($"{L.Get("difficulty")}: {AppSettings.TrainerDifficulty * 100:0} %", _label, GUILayout.Width(460));
            float difficulty = GUILayout.HorizontalSlider(AppSettings.TrainerDifficulty, 0f, 1f, GUILayout.Width(400));
            if (Mathf.Abs(difficulty - AppSettings.TrainerDifficulty) > 0.001f) AppSettings.TrainerDifficulty = Mathf.Round(difficulty * 20) / 20;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(GUILayout.Height(60));
            GUILayout.Label(L.Get("power_source"), _label, GUILayout.Width(460));
            string source = AppSettings.PowerSource == PowerSource.Trainer ? L.Get("trainer") : L.Get("power_meter");
            if (GUILayout.Button(source, _button, GUILayout.Width(300), GUILayout.Height(52)))
            {
                AppSettings.PowerSource = AppSettings.PowerSource == PowerSource.Trainer ? PowerSource.PowerMeter : PowerSource.Trainer;
            }

            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(GUILayout.Height(60));
            GUILayout.Label(L.Get("language"), _label, GUILayout.Width(460));
            if (GUILayout.Button(AppSettings.Language == "de" ? "Deutsch" : "English", _button, GUILayout.Width(300), GUILayout.Height(52)))
            {
                AppSettings.Language = AppSettings.Language == "de" ? "en" : "de";
            }

            GUILayout.EndHorizontal();

            if (_ble.BackendName == "Simulator")
            {
                GUILayout.BeginHorizontal(GUILayout.Height(60));
                GUILayout.Label($"{L.Get("sim_power")}: {SimulatedBleBackend.RiderPowerW:0} W", _label, GUILayout.Width(460));
                SimulatedBleBackend.RiderPowerW = Mathf.Round(GUILayout.HorizontalSlider(SimulatedBleBackend.RiderPowerW, 0f, 500f, GUILayout.Width(400)) / 5) * 5;
                GUILayout.EndHorizontal();
            }

            GUILayout.EndArea();
        }

        private int Stepper(string label, int value, string unit, int small, int large)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(60));
            GUILayout.Label(label, _label, GUILayout.Width(460));
            if (GUILayout.Button("−" + large, _button, GUILayout.Width(90), GUILayout.Height(52))) value -= large;
            if (GUILayout.Button("−" + small, _button, GUILayout.Width(90), GUILayout.Height(52))) value -= small;
            GUILayout.Label($"{value} {unit}", _label, GUILayout.Width(140));
            if (GUILayout.Button("+" + small, _button, GUILayout.Width(90), GUILayout.Height(52))) value += small;
            if (GUILayout.Button("+" + large, _button, GUILayout.Width(90), GUILayout.Height(52))) value += large;
            GUILayout.EndHorizontal();
            return value;
        }

        // ------------------------------------------------------------------ ride

        private void DrawRide()
        {
            var live = _ble.Hub.Live;
            var pos = _ride.Position;

            // Top tiles
            string[] labels = { L.Get("power"), L.Get("cadence"), L.Get("heart_rate"), L.Get("speed"), L.Get("distance"), L.Get("time") };
            string[] values =
            {
                $"{_ride.Power3s:0} W",
                live.CadenceRpm.HasValue ? $"{live.CadenceRpm:0}" : "–",
                live.HeartRateBpm.HasValue ? $"{live.HeartRateBpm}" : "–",
                $"{_ride.SpeedMps * 3.6:0.0}",
                $"{_ride.RideDistanceM / 1000:0.00} km",
                FormatTime(_ride.ElapsedS),
            };
            float tileW = Mathf.Min(170, (_w - 300) / labels.Length);
            for (int i = 0; i < labels.Length; i++)
            {
                var r = new Rect(20 + (i * (tileW + 8)), 15, tileW, 92);
                GUI.Box(r, GUIContent.none, _panel);
                GUI.Label(new Rect(r.x, r.y + 6, r.width, 24), labels[i], _tileLabel);
                GUI.Label(new Rect(r.x, r.y + 30, r.width, 56), values[i], _tileValue);
            }

            // Buttons top right
            if (GUI.Button(new Rect(_w - 250, 15, 230, 60), L.Get("end_ride"), _button)) _ride.EndRide();
            if (GUI.Button(new Rect(_w - 250, 82, 230, 60), _ride.IsPaused ? L.Get("resume") : L.Get("pause"), _button)) _ride.TogglePause();

            // Grade
            GUI.Label(new Rect(20, 115, 400, 40), $"{L.Get("grade")} {pos.GradePercent:+0.0;-0.0;0.0} %", _label);

            if (_ble.Hub.TrainerAddress == null) GUI.Label(new Rect(20, 150, 900, 40), L.Get("no_trainer"), _label);
            else if (_ride.IsAutoPaused) GUI.Label(new Rect(20, 150, 900, 40), L.Get("paused_pedal"), _label);
            else if (_ble.Hub.LowCadenceProtectionActive) GUI.Label(new Rect(20, 150, 900, 40), L.Get("low_cadence"), _label);

            if (_ride.Engine != null && _ride.WorkoutState != null) DrawWorkoutPanel();

            // Cue
            if (!string.IsNullOrEmpty(_ride.CueText) && Time.time < _ride.CueUntil)
            {
                var c = GUI.color;
                GUI.color = new Color(1, 1, 1, Mathf.Clamp01((_ride.CueUntil - Time.time) / 1.5f));
                GUI.Label(new Rect(0, (_h / 2) - 80, _w, 80), _ride.CueText, _cue);
                GUI.color = c;
            }
        }

        private void DrawWorkoutPanel()
        {
            var engine = _ride.Engine;
            var state = _ride.WorkoutState;
            int ftp = engine.FtpW;

            // Block panel (right side)
            var panel = new Rect(_w - 360, 155, 340, 300);
            GUI.Box(panel, GUIContent.none, _panel);
            GUILayout.BeginArea(new Rect(panel.x + 15, panel.y + 10, panel.width - 30, panel.height - 20));
            if (state.Segment != null)
            {
                string target = state.TargetW.HasValue
                    ? $"{state.TargetW} W  <size=20>({state.TargetW.Value * 100.0 / ftp:0} %)</size>"
                    : L.Get("free");
                GUILayout.Label($"{L.Get("target")}: {target}", _label);
                string repeat = state.Segment.Repeat.HasValue ? $"  #{state.Segment.Repeat}" : string.Empty;
                GUILayout.Label($"<size=44><b>{FormatTime(state.SegmentRemainingS)}</b></size>{repeat}", _label);
                if (state.Cadence.HasValue) GUILayout.Label($"{L.Get("cadence")}: {state.Cadence}", _small);
                if (state.NextSegment != null)
                {
                    var next = state.NextSegment;
                    double? nextW = next.BaseTargetW(0, ftp);
                    string nextText = nextW.HasValue ? $"{nextW.Value * engine.GlobalIntensity:0} W" : L.Get("free");
                    GUILayout.Label($"{L.Get("next")}: {FormatTime(next.DurationS)} @ {nextText}", _small);
                }

                GUILayout.BeginHorizontal();
                GUILayout.Label($"{L.Get("intensity")} {state.SegmentIntensity * 100:0} %", _small, GUILayout.Width(140));
                if (GUILayout.Button("−", _button, GUILayout.Height(50))) _ride.AdjustBlockIntensity(-0.02);
                if (GUILayout.Button("+", _button, GUILayout.Height(50))) _ride.AdjustBlockIntensity(0.02);
                GUILayout.EndHorizontal();
                if (GUILayout.Button(L.Get("skip"), _button, GUILayout.Height(50))) _ride.SkipBlock();
            }

            GUILayout.EndArea();

            // Workout graph (bottom)
            var graph = new Rect(20, _h - 120, _w - 40, 100);
            GUI.Box(graph, GUIContent.none, _panel);
            float total = engine.TotalDurationS;
            float x = graph.x + 5;
            float inner = graph.width - 10;
            var oldColor = GUI.color;
            for (int i = 0; i < engine.Segments.Count; i++)
            {
                var seg = engine.Segments[i];
                float width = inner * engine.SegmentDurationS(i) / total;
                int steps = seg.Block is RampBlock ? Mathf.Max(1, Mathf.CeilToInt(width / 6)) : 1;
                for (int s = 0; s < steps; s++)
                {
                    double? w = seg.BaseTargetW(seg.DurationS * (s + 0.5) / steps, ftp);
                    float pct = w.HasValue ? (float)(w.Value / ftp) : 1.2f;
                    float hgt = Mathf.Clamp(pct / 1.6f, 0.05f, 1f) * (graph.height - 10);
                    GUI.color = w.HasValue ? ZoneColor(w.Value, ftp) : new Color(0.6f, 0.6f, 0.6f, 0.6f);
                    GUI.DrawTexture(new Rect(x + (s * width / steps), graph.yMax - 5 - hgt, Mathf.Max(1, (width / steps) - 0.5f), hgt), Texture2D.whiteTexture);
                }

                x += width;
            }

            GUI.color = Color.white;
            float progressX = graph.x + 5 + (inner * (float)(engine.ElapsedS / total));
            GUI.DrawTexture(new Rect(progressX - 1.5f, graph.y + 2, 3, graph.height - 4), Texture2D.whiteTexture);
            GUI.color = oldColor;
        }

        private Color ZoneColor(double watts, int ftp)
        {
            var zone = _zones.ZoneFor(watts, ftp);
            return ColorUtility.TryParseHtmlString(zone.ColorHex, out var c) ? c : Color.gray;
        }

        private void DrawSummary()
        {
            var s = _ride.Summary;
            var area = new Rect((_w / 2) - 330, 40, 660, _h - 80);
            GUI.Box(area, GUIContent.none, _panel);
            GUILayout.BeginArea(new Rect(area.x + 25, area.y + 15, area.width - 50, area.height - 30));
            GUILayout.Label($"{L.Get("summary")}: {s.Title}", _title);
            GUILayout.Label($"{L.Get("time")}: {FormatTime(s.DurationS)}     {L.Get("distance")}: {s.DistanceM / 1000:0.00} km", _label);
            GUILayout.Label($"{L.Get("avg_power")}: {s.AvgPowerW:0} W     {L.Get("np")}: {s.NormalizedPowerW:0} W", _label);
            GUILayout.Label($"IF {s.IntensityFactor:0.00}     TSS {s.Tss:0}", _label);
            if (s.AvgHeartRate.HasValue) GUILayout.Label($"{L.Get("avg_hr")}: {s.AvgHeartRate:0} bpm", _label);
            if (s.PowerCurve != null && s.PowerCurve.Count > 0)
            {
                GUILayout.Label(string.Join("   ", s.PowerCurve.Select(kv => $"{FormatDuration(kv.Key)}: {kv.Value:0} W")), _small);
            }

            if (s.EstimatedFtpW.HasValue)
            {
                GUILayout.Space(10);
                GUILayout.Label($"{L.Get("new_ftp")}: <b>{s.EstimatedFtpW} W</b>  (vorher/before {AppSettings.FtpW} W)", _label);
                if (s.EstimatedFtpW != AppSettings.FtpW && GUILayout.Button(L.Get("apply_ftp"), _button, GUILayout.Height(60)))
                {
                    AppSettings.FtpW = s.EstimatedFtpW.Value;
                }
            }

            GUILayout.FlexibleSpace();
            if (!string.IsNullOrEmpty(s.FitPath))
            {
                GUILayout.Label($"FIT: {System.IO.Path.GetFileName(s.FitPath)}", _small);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(L.Get("share"), _button, GUILayout.Height(60))) _fileMessage = FileActions.Share(s.FitPath, L.Get("share"));
                if (GUILayout.Button(L.Get("save_downloads"), _button, GUILayout.Height(60))) _fileMessage = FileActions.SaveToDownloads(s.FitPath);
                GUILayout.EndHorizontal();
                if (!string.IsNullOrEmpty(_fileMessage)) GUILayout.Label(_fileMessage, _small);
            }

            if (GUILayout.Button(L.Get("back"), _button, GUILayout.Height(64)))
            {
                _ride.Close();
                _fileMessage = null;
                _page = Page.Home;
            }

            GUILayout.EndArea();
        }

        // ------------------------------------------------------------------ helpers

        private static string FormatTime(double seconds)
        {
            int s = Mathf.Max(0, Mathf.FloorToInt((float)seconds));
            return s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}";
        }

        private static string FormatDuration(int seconds) => seconds < 60 ? $"{seconds}s" : $"{seconds / 60}m";

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _stylesReady = true;

            var panelTexture = new Texture2D(1, 1);
            panelTexture.SetPixel(0, 0, new Color(0.05f, 0.07f, 0.1f, 0.78f));
            panelTexture.Apply();

            _panel = new GUIStyle(GUI.skin.box) { normal = { background = panelTexture } };
            _title = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold, richText = true, normal = { textColor = Color.white } };
            _label = new GUIStyle(GUI.skin.label) { fontSize = 24, richText = true, wordWrap = true, normal = { textColor = Color.white } };
            _small = new GUIStyle(_label) { fontSize = 17 };
            _tileLabel = new GUIStyle(_label) { fontSize = 17, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.75f, 0.8f, 0.85f) } };
            _tileValue = new GUIStyle(_label) { fontSize = 36, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _button = new GUIStyle(GUI.skin.button) { fontSize = 24, richText = true, wordWrap = true };
            _cue = new GUIStyle(_label) { fontSize = 46, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _textField = new GUIStyle(GUI.skin.textField) { fontSize = 24, alignment = TextAnchor.MiddleLeft };
            _paletteLabel = new GUIStyle(_label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.08f, 0.08f, 0.1f) } };
            _tinyCenter = new GUIStyle(_label) { fontSize = 15, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            _labelCenter = new GUIStyle(_label) { alignment = TextAnchor.MiddleCenter };
        }
    }
}

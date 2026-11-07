using System.Linq;
using FreeVelo.App;
using FreeVelo.Core.Physics;
using FreeVelo.Core.Training;
using FreeVelo.Core.Ui;
using FreeVelo.Devices.Zwift;
using UnityEngine;

namespace FreeVelo.UI
{
    /// <summary>Virtual gears (ride screen buttons, settings), Zwift controller status, backup.</summary>
    public sealed partial class Hud
    {
        private const float BackupCardH = 150;

        /// <summary>Ride screen: harder (+) / easier (−) with the current virtual gear, right side.</summary>
        private void DrawGearControl()
        {
            var gears = _ride.Gears;
            var card = new Rect(_w - 112, _h * 0.42f, 88, 220);
            Ui.Card(card, Theme.Alpha(Theme.Background, 0.88f));
            if (Ui.IconButton(new Rect(card.x + 12, card.y + 12, 64, 64), Icon.Plus, ButtonKind.Secondary, gears.Gear < gears.Count - 1)) _ride.Shift(+1);
            GUI.Label(new Rect(card.x, card.y + 82, card.width, 18), L.Get("gear").ToUpperInvariant(), Theme.With(Theme.Overline, anchor: TextAnchor.MiddleCenter));
            GUI.Label(new Rect(card.x, card.y + 98, card.width, 34), $"{gears.Gear + 1}", Theme.With(Theme.H1, size: 30, anchor: TextAnchor.MiddleCenter));
            if (Ui.IconButton(new Rect(card.x + 12, card.yMax - 76, 64, 64), Icon.Minus, ButtonKind.Secondary, gears.Gear > 0)) _ride.Shift(-1);
        }

        /// <summary>Settings: virtual gears on/off (the controller itself is on the devices page).</summary>
        private void DrawControlsCard(Rect card)
        {
            Ui.Card(card);
            SectionTitle(card, L.Get("virtual_gears"), Icon.Cadence);

            // One switch; how the gears reach the trainer is automatic (built into the trainer, or a matching grade).
            bool on = Ui.Segmented(new Rect(card.xMax - 24 - 180, card.y + 18, 180, 40), new[] { L.Get("off"), L.Get("on") }, AppSettings.VirtualGears ? 1 : 0) == 1;
            if (on != AppSettings.VirtualGears) AppSettings.VirtualGears = on;
            string hint = !on ? L.Get("gears_off_hint")
                : _ble.Hub.TrainerAddress == null ? L.Get("gears_on_hint")
                : _ble.Hub.TrainerSupportsShifting ? L.Get("gears_on_hint") + " " + L.Get("gears_trainer_hint")
                : L.Get("gears_on_hint") + " " + L.Get("gears_grade_hint");
            GUI.Label(new Rect(card.x + 24, card.y + 76, card.width - 48, 120), hint, Theme.With(Theme.Caption, Theme.Muted, wrap: true));

            // Workouts: the same with gears on or off, so on its own line.
            var line = new Rect(card.x + 24, card.yMax - 70, card.width - 48, 52);
            Ui.DrawIcon(new Rect(line.x, line.y + 4, 18, 18), Icon.Target, Theme.Accent);
            GUI.Label(new Rect(line.x + 28, line.y, line.width - 28, line.height), L.Get("gears_workout_hint"), Theme.With(Theme.Caption, Theme.Muted, wrap: true));
        }

        /// <summary>Devices page: the Zwift controller(s) next to trainer, power meter and heart rate.</summary>
        private void DrawControllerCard(Rect r)
        {
            var controllers = _ble.Hub.Controllers;
            bool any = controllers.Count > 0;
            bool locked = controllers.Any(c => c.State == ZwiftControllerState.Locked);
            bool ready = controllers.Any(c => c.State == ZwiftControllerState.Ready);
            var color = ready ? Theme.Success : locked ? Theme.Warning : Theme.Faint;
            Ui.Card(r);
            var circle = new Rect(r.x + 18, r.y + 22, 52, 52);
            Ui.Rect(circle, any ? Theme.Alpha(color, 0.16f) : Theme.Surface2, 26);
            Ui.DrawIcon(new Rect(circle.center.x - 13, circle.center.y - 13, 26, 26), Icon.Cadence, any ? color : Theme.Faint);
            GUI.Label(new Rect(r.x + 84, r.y + 20, r.width - 100, 18), "ZWIFT CLICK", Theme.Overline);
            if (!any)
            {
                GUI.Label(new Rect(r.x + 84, r.y + 40, r.width - 100, 28), L.Get("not_connected"), Theme.With(Theme.BodyMuted, size: 17));
                return;
            }

            // One line per unit (a Click V2 has two): state and battery
            float y = r.y + 40;
            foreach (var c in controllers.Take(2))
            {
                string state = L.Get("zwift_" + c.State.ToString().ToLowerInvariant());
                string battery = c.Battery.HasValue ? $" · {c.Battery} %" : "";
                var col = c.State == ZwiftControllerState.Ready ? Theme.Text : c.State == ZwiftControllerState.Locked ? Theme.Warning : Theme.Muted;
                GUI.Label(new Rect(r.x + 84, y, r.width - 100, 22), $"{state}{battery}", Clip(Theme.With(Theme.Body, col, 15)));
                y += 22;
            }

            // Locked unit: the explanation on tap (the card is small)
            if (locked && GUI.Button(r, GUIContent.none, GUIStyle.none)) Ui.Toast(L.Get("zwift_locked_hint"), 8f);
        }

        /// <summary>Settings: export a backup ZIP or restore one (picked with the import file picker).</summary>
        private void DrawBackupCard(Rect card)
        {
            Ui.Card(card);
            SectionTitle(card, L.Get("backup"), Icon.Download);
            GUI.Label(new Rect(card.x + 24, card.y + 70, card.width - 520, 60), L.Get("backup_hint"), Theme.With(Theme.Caption, Theme.Muted, wrap: true));
            float w1 = Ui.ButtonWidth(L.Get("backup_export"), true, 52), w2 = Ui.ButtonWidth(L.Get("backup_import"), true, 52);
            if (Ui.Button(new Rect(card.xMax - 24 - w2, card.y + 74, w2, 52), L.Get("backup_import"), ButtonKind.Secondary, Icon.Import)) WorkoutStore.PickFiles();
            if (Ui.Button(new Rect(card.xMax - 24 - w2 - 12 - w1, card.y + 74, w1, 52), L.Get("backup_export"), ButtonKind.Secondary, Icon.Share)) Ui.Toast(Backup.Export());
        }

        private const float ButtonsCardH = 250;
        private const float HrCardH = 250;

        private static string ButtonLabel(ZwiftButton b) => b switch
        {
            ZwiftButton.ShiftUpRight => "+",
            ZwiftButton.ShiftUpLeft => "−",
            ZwiftButton.Up => "↑",
            ZwiftButton.Down => "↓",
            ZwiftButton.Left => "←",
            ZwiftButton.Right => "→",
            _ => b.ToString(),
        };

        /// <summary>Settings → Trainer &amp; shifting: number of gears, ERG step and what each Zwift Click button does.</summary>
        private void DrawButtonsCard(Rect card)
        {
            Ui.Card(card);
            SectionTitle(card, L.Get("click_buttons"), Icon.Cadence);
            float x = card.x + 24;

            const float left = 330; // a stepper with ±4 buttons needs about 320
            GUI.Label(new Rect(x, card.y + 66, left, 22), L.Get("gear_count"), Theme.Caption);
            int gears = Ui.Stepper(new Rect(x, card.y + 90, left, 52), AppSettings.GearCount, null, 1, 4, AppSettings.MinGears, AppSettings.MaxGears);
            if (gears != AppSettings.GearCount) AppSettings.GearCount = gears;
            GUI.Label(new Rect(x, card.y + 154, left, 22), L.Get("erg_step"), Theme.Caption);
            int step = Ui.Stepper(new Rect(x, card.y + 178, left, 52), AppSettings.ErgStepPercent, "%", 1, 1, 1, 5);
            if (step != AppSettings.ErgStepPercent) AppSettings.ErgStepPercent = step;

            // Button tiles: tap cycles harder → easier → nothing
            var grid = new Rect(x + left + 30, card.y + 66, card.width - left - 30 - 48, card.height - 90);
            var buttons = AppSettings.MappableButtons;
            int cols = 5;
            float tw = (grid.width - (cols - 1) * 10) / cols, th = (grid.height - 10 - 30) / 2f;
            for (int i = 0; i < buttons.Length; i++)
            {
                var b = buttons[i];
                var r = new Rect(grid.x + (i % cols) * (tw + 10), grid.y + (i / cols) * (th + 10), tw, th);
                int action = AppSettings.ButtonAction(b);
                var color = action > 0 ? Theme.Accent : action < 0 ? Theme.Info : Theme.Surface2;
                Ui.Rect(r, action == 0 ? Theme.Surface2 : Theme.Alpha(color, 0.22f), Theme.RadiusSmall);
                if (action != 0) Ui.Outline(r, color, 2, Theme.RadiusSmall);
                GUI.Label(new Rect(r.x, r.y + 6, r.width, 30), ButtonLabel(b), Theme.With(Theme.H3, size: 22, anchor: TextAnchor.MiddleCenter));
                string what = action > 0 ? L.Get("harder") : action < 0 ? L.Get("easier") : "–";
                GUI.Label(new Rect(r.x, r.yMax - 28, r.width, 22), what, Theme.With(Theme.Caption, action == 0 ? Theme.Muted : Theme.Text, 13, TextAnchor.MiddleCenter));
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) AppSettings.SetButtonAction(b, action > 0 ? -1 : action < 0 ? 0 : 1);
            }

            GUI.Label(new Rect(grid.x, grid.yMax - 24, grid.width - 160, 24), L.Get("click_buttons_hint"), Theme.With(Theme.Caption, Theme.Muted, 13));
            if (Ui.Button(new Rect(grid.xMax - 150, grid.yMax - 30, 150, 36), L.Get("reset_default"), ButtonKind.Ghost, Icon.Reset)) AppSettings.ResetButtonMap();
        }

        /// <summary>Settings → Profile: heart rate zones from max HR or LTHR.</summary>
        private void DrawHeartRateCard(Rect card)
        {
            Ui.Card(card);
            SectionTitle(card, L.Get("hr_zones"), Icon.Heart);
            float x = card.x + 24;
            const float left = 370; // stepper with ±5 and "185 bpm" needs about 350
            int basis = Ui.Segmented(new Rect(x, card.y + 70, left, 46), new[] { L.Get("max_hr_short"), "LTHR" }, (int)AppSettings.HrZoneBasis);
            if (basis != (int)AppSettings.HrZoneBasis) AppSettings.HrZoneBasis = (HeartRateZoneBasis)basis;
            bool lthr = AppSettings.HrZoneBasis == HeartRateZoneBasis.Lthr;
            GUI.Label(new Rect(x, card.y + 128, left, 22), lthr ? L.Get("lthr_long") : L.Get("max_hr"), Theme.Caption);
            int value = Ui.Stepper(new Rect(x, card.y + 152, left, 52), lthr ? AppSettings.Lthr : AppSettings.MaxHr, "bpm", 1, 5, lthr ? 100 : 120, lthr ? 220 : 230);
            if (lthr && value != AppSettings.Lthr) AppSettings.Lthr = value;
            if (!lthr && value != AppSettings.MaxHr) AppSettings.MaxHr = value;
            GUI.Label(new Rect(x, card.y + 210, left, 34), lthr ? L.Get("lthr_hint") : L.Get("max_hr_hint"), Theme.With(Theme.Caption, Theme.Muted, 12, wrap: true));

            // Zones list
            var zones = AppSettings.HrZones.Zones;
            var list = new Rect(x + left + 40, card.y + 70, card.width - left - 40 - 48, card.height - 90);
            float rh = Mathf.Min(30, list.height / zones.Count);
            for (int i = 0; i < zones.Count; i++)
            {
                var z = zones[i];
                float y = list.y + i * rh;
                ColorUtility.TryParseHtmlString(z.ColorHex, out var col);
                Ui.Rect(new Rect(list.x, y + 6, 6, rh - 12), col, 3);
                GUI.Label(new Rect(list.x + 16, y, 60, rh), $"Z{z.Number}", Theme.With(Theme.Body, size: 15));
                GUI.Label(new Rect(list.x + 60, y, list.width - 200, rh), z.Name.Get(AppSettings.Language), Clip(Theme.With(Theme.Body, Theme.Muted, 15)));
                string range = z.UpperBpm.HasValue ? (i == 0 ? $"< {z.UpperBpm}" : $"{z.LowerBpm}–{z.UpperBpm - 1}") : $"≥ {z.LowerBpm}";
                GUI.Label(new Rect(list.xMax - 140, y, 140, rh), range + " bpm", Theme.With(Theme.Body, size: 15, anchor: TextAnchor.MiddleRight));
            }
        }
    }
}

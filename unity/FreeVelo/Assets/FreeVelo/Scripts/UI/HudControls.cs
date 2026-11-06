using System.Linq;
using FreeVelo.App;
using FreeVelo.Core.Physics;
using FreeVelo.Core.Ui;
using FreeVelo.Devices.Zwift;
using UnityEngine;

namespace FreeVelo.UI
{
    /// <summary>Virtual gears (ride screen buttons, settings), Zwift controller status, backup.</summary>
    public sealed partial class Hud
    {
        private const float ControlsCardH = 220;
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

        /// <summary>Settings: virtual gear mode (the controller itself is on the devices page).</summary>
        private void DrawControlsCard(Rect card)
        {
            Ui.Card(card);
            SectionTitle(card, L.Get("controls"), Icon.Cadence);

            GUI.Label(new Rect(card.x + 24, card.y + 68, 400, 22), L.Get("virtual_gears"), Theme.Caption);
            var options = new[] { L.Get("off"), L.Get("gears_grade"), L.Get("gears_erg") };
            int mode = Ui.Segmented(new Rect(card.x + 24, card.y + 94, 480, 50), options, (int)AppSettings.GearMode);
            if (mode != (int)AppSettings.GearMode) AppSettings.GearMode = (VirtualGearMode)mode;
            string hint = AppSettings.GearMode switch
            {
                VirtualGearMode.Grade => L.Get("gears_grade_hint"),
                VirtualGearMode.Erg => L.Get("gears_erg_hint"),
                _ => L.Get("gears_off_hint"),
            };
            GUI.Label(new Rect(card.x + 24, card.y + 152, card.width - 48, 64), hint, Theme.With(Theme.Caption, Theme.Muted, wrap: true));
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
    }
}

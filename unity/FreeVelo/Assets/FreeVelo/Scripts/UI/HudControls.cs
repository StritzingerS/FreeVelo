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
        private const float ControlsCardH = 236;
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

        /// <summary>Settings: virtual gear mode and the Zwift controllers.</summary>
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
            GUI.Label(new Rect(card.x + 24, card.y + 152, 480, 64), hint, Theme.With(Theme.Caption, Theme.Muted, wrap: true));

            // Zwift controllers
            var right = new Rect(card.x + 540, card.y + 68, card.width - 540 - 24, card.height - 90);
            GUI.Label(new Rect(right.x, right.y, right.width, 22), "Zwift Click / Play", Theme.Caption);
            var controllers = _ble.Hub.Controllers;
            float y = right.y + 28;
            if (controllers.Count == 0)
            {
                GUI.Label(new Rect(right.x, y, right.width, 80), L.Get("zwift_none"), Theme.With(Theme.Caption, Theme.Muted, wrap: true));
                return;
            }

            foreach (var c in controllers.Take(3))
            {
                var color = c.State == ZwiftControllerState.Ready ? Theme.Success : c.State == ZwiftControllerState.Locked ? Theme.Warning : Theme.Faint;
                Ui.Dot(new Vector2(right.x + 6, y + 12), 5, color);
                string state = L.Get("zwift_" + c.State.ToString().ToLowerInvariant());
                string battery = c.Battery.HasValue ? $" · {c.Battery} %" : "";
                GUI.Label(new Rect(right.x + 18, y, right.width - 18, 24), $"{_ble.NameOf(c.Address)} – {state}{battery}", Clip(Theme.With(Theme.Body, size: 15)));
                y += 28;
            }

            if (controllers.Any(c => c.State == ZwiftControllerState.Locked))
            {
                GUI.Label(new Rect(right.x, y, right.width, 54), L.Get("zwift_locked_hint"), Theme.With(Theme.Caption, Theme.Warning, wrap: true));
            }
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

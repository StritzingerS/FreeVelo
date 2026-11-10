using FreeVelo.App;
using FreeVelo.Core.Ui;
using FreeVelo.Net;
using FreeVelo.Shared.Garmin;
using UnityEngine;

namespace FreeVelo.UI
{
    /// <summary>Settings → Data: Garmin Connect import (personal API key, status, sync now).</summary>
    public sealed partial class Hud
    {
        private const float GarminCardH = 200;
        private GarminService _garmin;
        private string _garminKeyInput = string.Empty;

        public void InitializeGarmin(GarminService garmin) => _garmin = garmin;

        private void DrawGarminCard(Rect card)
        {
            Ui.Card(card);
            SectionTitle(card, "Garmin Connect", Icon.Import);
            if (_garmin == null) return;
            float x = card.x + 24, w = card.width - 48, y = card.y + 70;

            if (!_garmin.Configured)
            {
                // How to get a key, the field to paste it, and the link to Garmin's key page.
                GUI.Label(new Rect(x, y, w, 40), L.Get("garmin_hint"), Theme.With(Theme.Caption, Theme.Muted, wrap: true));
                var field = new Rect(x, y + 50, w - 2 * 200 - 24, 52);
                _garminKeyInput = Ui.TextField(field, _garminKeyInput, 600);
                if (Ui.Button(new Rect(field.xMax + 12, field.y, 200, 52), L.Get("garmin_create_key"), ButtonKind.Secondary, Icon.Share))
                {
                    Application.OpenURL(GarminPersonalApi.KeyPage);
                }

                if (Ui.Button(new Rect(field.xMax + 224, field.y, 200, 52), L.Get("garmin_connect"), ButtonKind.Primary, Icon.Check, _garminKeyInput.Trim().Length >= 16))
                {
                    _garmin.SetKey(_garminKeyInput);
                    _garminKeyInput = string.Empty;
                }

                return;
            }

            // Connected: status, sync now, remove.
            string status = _garmin.Syncing ? L.Get("garmin_syncing")
                : !string.IsNullOrEmpty(_garmin.LastError) ? _garmin.LastError
                : _garmin.LastSync.HasValue ? string.Format(L.Get("garmin_status"), _garmin.LastSync.Value.ToString("dd.MM. HH:mm"), _garmin.LastImported)
                : L.Get("garmin_connected");
            var color = !string.IsNullOrEmpty(_garmin.LastError) && !_garmin.Syncing ? Theme.Warning : Theme.Muted;
            GUI.Label(new Rect(x, y, w, 44), status, Theme.With(Theme.Body, color, 16, wrap: true));
            GUI.Label(new Rect(x, y + 46, w, 22), L.Get("garmin_what"), Theme.With(Theme.Caption, Theme.Muted));
            if (Ui.Button(new Rect(card.xMax - 24 - 200, card.yMax - 72, 200, 52), L.Get("garmin_remove"), ButtonKind.Secondary, Icon.Trash)) _garmin.RemoveKey();
            if (Ui.Button(new Rect(card.xMax - 24 - 200 - 12 - 220, card.yMax - 72, 220, 52), L.Get("garmin_sync_now"), ButtonKind.Primary, Icon.Download, !_garmin.Syncing))
            {
                _garmin.Sync();
            }
        }
    }
}

using FreeVelo.App;
using FreeVelo.Core.Ui;
using FreeVelo.Net;
using FreeVelo.Shared.Strava;
using UnityEngine;

namespace FreeVelo.UI
{
    /// <summary>Strava: connect / disconnect in the settings, upload status of the saved rides.</summary>
    public sealed partial class Hud
    {
        /// <summary>Strava's brand orange (for the "Connect with Strava" button).</summary>
        private static readonly Color StravaOrange = new Color32(0xFC, 0x4C, 0x02, 0xFF);

        private StravaService _strava;
        private bool _confirmStravaDisconnect;

        public void InitializeStrava(StravaService strava) => _strava = strava;

        private const float StravaCardH = 150;

        private void DrawStravaCard(Rect card)
        {
            Ui.Card(card);
            SectionTitle(card, "Strava", Icon.Import);
            if (_strava == null) return;

            var text = new Rect(card.x + 24, card.y + 76, card.width - 48, 52);
            if (_strava.Connected)
            {
                GUI.Label(new Rect(text.x, text.y, text.width * 0.45f, text.height), string.Format(L.Get("strava_connected_as"), $"<b>{_strava.AthleteName}</b>"), Theme.With(Theme.Body, anchor: TextAnchor.MiddleLeft));

                float disconnectW = Ui.ButtonWidth(_confirmStravaDisconnect ? L.Get("confirm_disconnect") : L.Get("disconnect"), false);
                var disconnect = new Rect(text.xMax - disconnectW, text.y, disconnectW, 52);
                if (Ui.Button(disconnect, _confirmStravaDisconnect ? L.Get("confirm_disconnect") : L.Get("disconnect"), _confirmStravaDisconnect ? ButtonKind.Danger : ButtonKind.Secondary))
                {
                    if (_confirmStravaDisconnect) _strava.Disconnect();
                    _confirmStravaDisconnect = !_confirmStravaDisconnect;
                }

                var mode = new Rect(disconnect.x - 16 - 340, text.y, 340, 52);
                int auto = Ui.Segmented(mode, new[] { L.Get("strava_auto"), L.Get("strava_manual") }, _strava.AutoUpload ? 0 : 1);
                if ((auto == 0) != _strava.AutoUpload) _strava.AutoUpload = auto == 0;
            }
            else if (_strava.Connecting)
            {
                float pulse = 0.5f + (0.5f * Mathf.Sin(Time.unscaledTime * 4f));
                Ui.Dot(new Vector2(text.x + 6, text.center.y), 6, Theme.Alpha(StravaOrange, 0.4f + (0.6f * pulse)));
                GUI.Label(new Rect(text.x + 22, text.y, text.width - 220, text.height), L.Get("strava_waiting"), Theme.With(Theme.Body, anchor: TextAnchor.MiddleLeft));
                if (Ui.Button(new Rect(text.xMax - 180, text.y, 180, 52), L.Get("cancel"), ButtonKind.Secondary)) _strava.CancelConnect();
            }
            else
            {
                float buttonW = Ui.ButtonWidth(L.Get("strava_connect"), false, 52) + 24;
                GUI.Label(new Rect(text.x, text.y, text.width - buttonW - 16, text.height), L.Get("strava_hint"), Theme.With(Theme.Caption, anchor: TextAnchor.MiddleLeft));
                var button = new Rect(text.xMax - buttonW, text.y, buttonW, 52);
                Ui.Rect(button, StravaOrange, Theme.RadiusSmall);
                GUI.Label(button, L.Get("strava_connect"), Theme.With(Theme.Body, Color.white, 17, TextAnchor.MiddleCenter));
                if (GUI.Button(button, GUIContent.none, GUIStyle.none)) _strava.Connect();
            }
        }

        /// <summary>Short Strava status of a saved ride ("" if Strava is not connected and it was never uploaded).</summary>
        private string StravaStatusText(string fitPath)
        {
            var item = _strava?.Status(fitPath);
            if (item == null) return "";
            switch (item.Status)
            {
                case StravaItemStatus.Done: return "Strava ✓";
                case StravaItemStatus.Failed: return L.Get("strava_failed_short");
                default: return item.attempts > 0 ? L.Get("strava_waiting_short") : L.Get("strava_uploading");
            }
        }

        /// <summary>Strava button for a saved ride: view the activity, or upload it. Returns false if none was drawn.</summary>
        private bool StravaRideButton(Rect r, string fitPath, string name)
        {
            if (_strava == null) return false;
            var item = _strava.Status(fitPath);
            if (item != null && item.Status == StravaItemStatus.Done && item.activityId > 0)
            {
                if (Ui.IconButton(r, Icon.Check, ButtonKind.Secondary)) StravaService.OpenActivity(item.activityId);
                return true;
            }

            if (!_strava.Connected) return false;
            bool busy = item != null && (item.Status == StravaItemStatus.Waiting || item.Status == StravaItemStatus.Processing);
            if (Ui.IconButton(r, Icon.Import, ButtonKind.Secondary, enabled: !busy))
            {
                _strava.Upload(fitPath, name);
                Ui.Toast(L.Get("strava_uploading"));
            }

            return true;
        }

        /// <summary>The Strava action on the ride summary (next to share / Downloads).</summary>
        private void DrawSummaryStravaButton(Rect r, string fitPath, string name)
        {
            if (_strava == null) return;
            var item = _strava.Status(fitPath);
            if (item != null && item.Status == StravaItemStatus.Done && item.activityId > 0)
            {
                if (Ui.Button(r, L.Get("strava_view"), ButtonKind.Secondary, Icon.Check)) StravaService.OpenActivity(item.activityId);
            }
            else if (item != null && item.Status != StravaItemStatus.Failed)
            {
                Ui.Button(r, L.Get("strava_uploading"), ButtonKind.Secondary, Icon.Import, enabled: false);
            }
            else if (_strava.Connected && Ui.Button(r, L.Get("strava_upload"), ButtonKind.Secondary, Icon.Import))
            {
                _strava.Upload(fitPath, name);
            }
        }
    }
}

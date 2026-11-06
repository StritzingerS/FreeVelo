using System.Linq;
using FreeVelo.App;
using FreeVelo.Core.Ui;
using FreeVelo.Net;
using FreeVelo.Shared.Api;
using FreeVelo.Shared.Live;
using FreeVelo.World;
using UnityEngine;

namespace FreeVelo.UI
{
    /// <summary>
    /// Group rides in the interface: first start (server + name), create or join with the 4-letter code, the waiting
    /// room with the code to share, and during the ride the rider list, name tags above the others and emojis.
    /// </summary>
    public sealed partial class Hud
    {
        private static readonly (Emoji Emoji, Icon Icon, Color Color)[] Emojis =
        {
            (Emoji.ThumbsUp, Icon.ThumbsUp, Theme.Warning),
            (Emoji.Fire, Icon.Fire, Theme.Accent),
            (Emoji.Heart, Icon.Heart, Theme.HeartRed),
            (Emoji.Bolt, Icon.Bolt, Theme.Warning),
            (Emoji.Sweat, Icon.Drop, Theme.Info),
            (Emoji.Wave, Icon.Hand, Theme.Success),
        };

        private GroupRideService _group;
        private GroupAvatars _groupAvatars;
        private string _nameInput;
        private string _serverInput;
        private string _codeInput = string.Empty;
        private string _rideNameInput;
        private int _newMode;
        private int _newVisibility;
        private float _nextListRefresh;
        private float _guiScale = 1f;
        private Vector2 _guiOffset;

        public void InitializeGroup(GroupRideService group, GroupAvatars avatars)
        {
            _group = group;
            _groupAvatars = avatars;
        }

        // ------------------------------------------------------------------ menu page

        private void DrawGroup(Rect c)
        {
            Header(c, L.Get("group_rides"), L.Get("group_intro"));
            var body = new Rect(c.x, c.y + 92, c.width, c.height - 92);
            if (!_group.HasServer || !_group.HasIdentity) DrawGroupSetup(body);
            else if (_group.Lobby != null) DrawWaitingRoom(body);
            else DrawGroupStart(body);
        }

        /// <summary>First start: what the others call you (the server is preset; an own one is set in Settings).</summary>
        private void DrawGroupSetup(Rect c)
        {
            _nameInput ??= string.Empty;
            var card = new Rect(c.x, c.y, Mathf.Min(c.width, 640), 290);
            Ui.Card(card);
            SectionTitle(card, L.Get("your_name"), Icon.Group);

            float x = card.x + 24, w = card.width - 48;
            _nameInput = Field(new Rect(x, card.y + 70, w, 56), _nameInput, RiderRules.MaxDisplayNameLength, "Sami");
            GUI.Label(new Rect(x, card.y + 130, w, 44), L.Get("name_hint"), Theme.With(Theme.Caption, size: 14));
            GUI.Label(new Rect(x, card.yMax - 70, w - 240, 50), $"{L.Get("server")}: {ServerHost()}\n<color=#76828F>{L.Get("server_change_hint")}</color>", Theme.With(Theme.Caption, size: 13, anchor: TextAnchor.MiddleLeft));

            bool ready = RiderRules.MinDisplayNameLength <= _nameInput.Trim().Length && !_group.Busy;
            if (Ui.Button(new Rect(card.xMax - 224, card.yMax - 72, 200, 52), L.Get("continue"), ButtonKind.Primary, Icon.ChevronRight, ready))
            {
                _group.CreateIdentity(_nameInput);
            }
        }

        private static string ServerHost()
        {
            string url = AppSettings.ServerUrl;
            return System.Uri.TryCreate(url, System.UriKind.Absolute, out var uri) ? uri.Host + (uri.IsDefaultPort ? string.Empty : ":" + uri.Port) : url;
        }

        /// <summary>Not in a ride: join with a code, create one, or pick an open ride.</summary>
        private void DrawGroupStart(Rect c)
        {
            float colW = (c.width - 24) / 2f;
            string me = AppSettings.Rider?.displayName ?? string.Empty;

            // Join with code
            var join = new Rect(c.x, c.y, colW, 300);
            Ui.Card(join);
            SectionTitle(join, L.Get("join_with_code"), Icon.Group);
            GUI.Label(new Rect(join.x + 24, join.y + 64, join.width - 48, 22), L.Get("code_hint"), Theme.Caption);
            var codeRect = new Rect(join.x + 24, join.y + 96, join.width - 48, 84);
            string typed = Ui.TextField(codeRect, GroupRideService.FormatCode(_codeInput), 6);
            _codeInput = LobbyRules.NormalizeCode(typed);
            if (_codeInput.Length > LobbyRules.CodeLength) _codeInput = _codeInput.Substring(0, LobbyRules.CodeLength);
            if (_codeInput.Length == 0) GUI.Label(codeRect, "AB CD", Theme.With(Theme.Display, Theme.Faint, 44, TextAnchor.MiddleCenter));
            bool codeReady = _codeInput.Length == LobbyRules.CodeLength && !_group.Busy;
            if (Ui.Button(new Rect(join.x + 24, join.yMax - 84, join.width - 48, 56), L.Get("join"), ButtonKind.Primary, Icon.ChevronRight, codeReady))
            {
                _group.JoinByCode(_codeInput);
                _codeInput = string.Empty;
            }

            // Create
            var create = new Rect(c.x + colW + 24, c.y, colW, 300);
            Ui.Card(create);
            SectionTitle(create, L.Get("create_ride"), Icon.Plus);
            _rideNameInput ??= string.Format(L.Get("default_ride_name"), me);
            float x = create.x + 24, w = create.width - 48;
            _rideNameInput = Field(new Rect(x, create.y + 62, w, 48), _rideNameInput, LobbyRules.MaxNameLength, L.Get("ride_name"));
            _newMode = Ui.Segmented(new Rect(x, create.y + 120, w, 44), new[] { L.Get("keep_together"), L.Get("free_mode") }, _newMode);
            GUI.Label(new Rect(x, create.y + 166, w, 40), L.Get(_newMode == 0 ? "keep_together_hint" : "free_mode_hint"), Theme.With(Theme.Caption, size: 13));
            _newVisibility = Ui.Segmented(new Rect(x, create.yMax - 84, w - 172, 56), new[] { L.Get("private"), L.Get("public") }, _newVisibility);
            bool nameOk = _rideNameInput.Trim().Length >= LobbyRules.MinNameLength;
            if (Ui.Button(new Rect(create.xMax - 180, create.yMax - 84, 156, 56), L.Get("create"), ButtonKind.Primary, null, nameOk && !_group.Busy))
            {
                _group.CreateLobby(new CreateLobbyRequest
                {
                    name = _rideNameInput.Trim(),
                    mode = _newMode == 0 ? LobbyMode.KeepTogether : LobbyMode.Free,
                    isPrivate = _newVisibility == 0,
                });
            }

            // Open (public) rides
            if (Time.unscaledTime > _nextListRefresh)
            {
                _nextListRefresh = Time.unscaledTime + 10f;
                _group.RefreshPublicLobbies();
            }

            float y = join.yMax + 26;
            GUI.Label(new Rect(c.x, y, 400, 20), L.Get("open_rides").ToUpperInvariant(), Theme.Overline);
            if (Ui.Button(new Rect(c.xMax - 170, y - 12, 170, 40), L.Get("refresh"), ButtonKind.Ghost, Icon.Reset)) _nextListRefresh = 0;
            y += 36;
            var lobbies = _group.PublicLobbies;
            if (lobbies.Length == 0)
            {
                GUI.Label(new Rect(c.x, y, c.width, 30), L.Get("no_open_rides"), Theme.BodyMuted);
                return;
            }

            foreach (var lobby in lobbies.Take(4))
            {
                var row = new Rect(c.x, y, c.width, 64);
                Ui.Card(row);
                Ui.DrawIcon(new Rect(row.x + 20, row.center.y - 12, 24, 24), lobby.mode == LobbyMode.KeepTogether ? Icon.Group : Icon.Bike, Theme.Accent);
                GUI.Label(new Rect(row.x + 58, row.y + 8, row.width - 300, 26), lobby.name, Clip(Theme.With(Theme.H3, size: 17)));
                string meta = $"{lobby.hostName} · {L.Get(lobby.mode == LobbyMode.KeepTogether ? "keep_together" : "free_mode")} · {string.Format(L.Get("riders_count"), lobby.riderCount, lobby.maxRiders)}";
                GUI.Label(new Rect(row.x + 58, row.y + 34, row.width - 300, 22), meta, Clip(Theme.Caption));
                bool full = lobby.riderCount >= lobby.maxRiders;
                if (Ui.Button(new Rect(row.xMax - 156, row.y + 10, 140, 44), L.Get("join"), ButtonKind.Secondary, null, !full && !_group.Busy)) _group.JoinPublic(lobby.id);
                y += 74;
            }
        }

        /// <summary>In a group ride, before or after riding: the code to share, who is there, start or leave.</summary>
        private void DrawWaitingRoom(Rect c)
        {
            var lobby = _group.Lobby;
            float colW = (c.width - 24) / 2f;

            var info = new Rect(c.x, c.y, colW, 400);
            Ui.Card(info);
            GUI.Label(new Rect(info.x + 24, info.y + 20, info.width - 48, 34), lobby.name, Clip(Theme.H2));
            float chipX = info.x + 24;
            chipX += Ui.Chip(new Rect(chipX, info.y + 60, 0, 30), L.Get(lobby.mode == LobbyMode.KeepTogether ? "keep_together" : "free_mode"), Theme.Accent, Icon.Group) + 8;
            Ui.Chip(new Rect(chipX, info.y + 60, 0, 30), L.Get(lobby.isPrivate ? "private" : "public"), Theme.Muted);

            GUI.Label(new Rect(info.x + 24, info.y + 112, 300, 20), L.Get("code").ToUpperInvariant(), Theme.Overline);
            var codeBox = new Rect(info.x + 24, info.y + 138, info.width - 48, 96);
            Ui.Rect(codeBox, Theme.Surface2, Theme.RadiusSmall);
            GUI.Label(codeBox, GroupRideService.FormatCode(lobby.code), Theme.With(Theme.Display, Theme.Text, 60, TextAnchor.MiddleCenter));
            if (Ui.Button(new Rect(info.x + 24, info.y + 248, info.width - 48, 52), L.Get("share_code"), ButtonKind.Secondary, Icon.Share))
            {
                GUIUtility.systemCopyBuffer = _group.InviteText();
                string error = FileActions.ShareText(_group.InviteText(), L.Get("share_code"));
                Ui.Toast(error ?? "✓ " + L.Get("code_copied"));
            }

            GUI.Label(new Rect(info.x + 24, info.y + 312, info.width - 48, 64), L.Get("group_waiting"), Theme.With(Theme.Caption, size: 14));

            // Riders and actions
            var people = new Rect(c.x + colW + 24, c.y, colW, 400);
            Ui.Card(people);
            SectionTitle(people, $"{L.Get("riders")} · {lobby.riders.Length}/{lobby.maxRiders}", Icon.Group);
            float y = people.y + 70;
            string myId = AppSettings.Rider?.riderId;
            foreach (var rider in lobby.riders.Take(6))
            {
                string label = rider.displayName + (rider.id == myId ? $"  <color=#76828F>({L.Get("you")})</color>" : string.Empty);
                Ui.Dot(new Vector2(people.x + 34, y + 16), 6, rider.id == lobby.hostRiderId ? Theme.Accent : Theme.Success);
                GUI.Label(new Rect(people.x + 52, y, people.width - 180, 32), label, Clip(Theme.Body));
                if (rider.id == lobby.hostRiderId) GUI.Label(new Rect(people.xMax - 124, y, 100, 32), L.Get("host"), Theme.With(Theme.Caption, anchor: TextAnchor.MiddleRight));
                y += 38;
            }

            if (Ui.Button(new Rect(people.x + 24, people.yMax - 84, 160, 56), L.Get("leave"), ButtonKind.Secondary, null, !_group.Busy)) _group.Leave();
            if (Ui.Button(new Rect(people.xMax - 244, people.yMax - 84, 220, 56), L.Get("start_riding"), ButtonKind.Primary, Icon.Play)) _ride.StartRide(null);
        }

        /// <summary>Settings card: server address and display name.</summary>
        private void DrawGroupSettingsCard(Rect card)
        {
            Ui.Card(card);
            SectionTitle(card, L.Get("group_rides"), Icon.Group);
            float colW = (card.width - 72) / 2f;
            float x = card.x + 24;

            // Empty field = the default server (shown as placeholder); an own address replaces it.
            _serverInput ??= AppSettings.HasOwnServer ? AppSettings.ServerUrl : string.Empty;
            GUI.Label(new Rect(x, card.y + 66, colW, 22), L.Get("server"), Theme.Caption);
            _serverInput = Field(new Rect(x, card.y + 92, colW - 112, 52), _serverInput, 120, AppSettings.DefaultServerUrl);
            string wanted = _serverInput.Trim().Length == 0 ? AppSettings.DefaultServerUrl : NormalizeServer(_serverInput);
            if (Ui.Button(new Rect(x + colW - 100, card.y + 92, 100, 52), "OK", ButtonKind.Secondary, null, wanted != AppSettings.ServerUrl))
            {
                AppSettings.ServerUrl = wanted;
                _serverInput = AppSettings.HasOwnServer ? AppSettings.ServerUrl : string.Empty;
                Ui.Toast("✓ " + L.Get("saved"));
            }

            GUI.Label(new Rect(x, card.y + 146, colW, 22), L.Get("server_own_hint"), Theme.With(Theme.Caption, size: 13));

            var rider = AppSettings.Rider;
            if (rider == null) return;
            float x2 = x + colW + 24;
            _nameInput ??= rider.displayName;
            GUI.Label(new Rect(x2, card.y + 66, colW, 22), L.Get("your_name"), Theme.Caption);
            _nameInput = Field(new Rect(x2, card.y + 92, colW - 112, 52), _nameInput, RiderRules.MaxDisplayNameLength, rider.displayName);
            bool changed = _nameInput.Trim() != rider.displayName && _nameInput.Trim().Length >= RiderRules.MinDisplayNameLength;
            if (Ui.Button(new Rect(x2 + colW - 100, card.y + 92, 100, 52), "OK", ButtonKind.Secondary, null, changed && !_group.Busy)) _group.Rename(_nameInput);
        }

        // ------------------------------------------------------------------ during the ride

        private void DrawGroupOverlay()
        {
            double now = GroupRideService.NowS;
            var riders = _group.State.Riders(now);
            bool hasMe = _group.State.TryGetMine(now, out var me);

            // Name tags (and fresh emojis) above the other riders.
            var cam = Camera.main;
            var recent = _group.State.EmojisSince(now - 3.5);
            if (cam != null && _groupAvatars != null)
            {
                foreach (var (rider, head) in _groupAvatars.Visible)
                {
                    var sp = cam.WorldToScreenPoint(head);
                    if (sp.z <= 0.5f || sp.z > 160f) continue;
                    var gp = new Vector2((sp.x - _guiOffset.x) / _guiScale, (Screen.height - sp.y - _guiOffset.y) / _guiScale);
                    float alpha = Mathf.Clamp01((160f - sp.z) / 40f);
                    var old = GUI.color;
                    GUI.color = new Color(1, 1, 1, alpha);
                    var style = Theme.With(Theme.Caption, Theme.Text, 15, TextAnchor.MiddleLeft);
                    string text = $"{rider.DisplayName}  <color=#B3BECA>{rider.Wkg:0.0}</color>";
                    float w = style.CalcSize(new GUIContent(text)).x + 34;
                    var tag = new Rect(gp.x - (w / 2), gp.y - 30, w, 30);
                    Ui.Rect(tag, Theme.Alpha(Theme.Background, 0.8f), 15);
                    Ui.Dot(new Vector2(tag.x + 14, tag.center.y), 5, GroupAvatars.JerseyOf(rider.Slot));
                    GUI.Label(new Rect(tag.x + 24, tag.y, w - 24, tag.height), text, style);
                    foreach (var e in recent.Where(e => e.Slot == rider.Slot).Take(1))
                    {
                        float rise = (float)(now - e.ReceivedS) * 12f;
                        EmojiBubble(new Rect(gp.x - 24, tag.y - 56 - rise, 48, 48), e.Emoji);
                    }

                    GUI.color = old;
                }
            }

            // Rider list (left): who is where, relative to me.
            var panel = new Rect(24, 100, 300, 44 + (Mathf.Min(riders.Count, 8) * 38));
            Ui.Card(panel, Theme.Alpha(Theme.Background, 0.85f));
            GUI.Label(new Rect(panel.x + 16, panel.y + 12, panel.width - 32, 20), (_group.Lobby?.name ?? L.Get("group")).ToUpperInvariant(), Clip(Theme.Overline));
            float y = panel.y + 40;
            foreach (var r in riders.OrderByDescending(r => r.DistanceM).Take(8))
            {
                var row = new Rect(panel.x + 12, y, panel.width - 24, 34);
                if (r.IsMe) Ui.Rect(row, Theme.Alpha(Theme.Accent, 0.15f), 8);
                Ui.Dot(new Vector2(row.x + 14, row.center.y), 6, GroupAvatars.JerseyOf(r.Slot));
                bool towed = (r.Flags & RiderFlags.Towed) != 0;
                bool away = (r.Flags & RiderFlags.Connected) == 0;
                var nameColor = away ? Theme.Faint : Theme.Text;
                GUI.Label(new Rect(row.x + 28, row.y, 130, row.height), r.DisplayName, Clip(Theme.With(Theme.Body, nameColor, 15)));
                GUI.Label(new Rect(row.x + 154, row.y, 54, row.height), $"{r.Wkg:0.0}", Theme.With(Theme.Caption, Theme.Muted, 14, TextAnchor.MiddleRight));
                string gap = r.IsMe || !hasMe ? string.Empty : $"{r.DistanceM - me.DistanceM:+0;-0;0} m";
                if (towed) Ui.DrawIcon(new Rect(row.xMax - 24, row.center.y - 9, 18, 18), Icon.Drop, Theme.Info);
                else GUI.Label(new Rect(row.x + 206, row.y, row.width - 210, row.height), gap, Theme.With(Theme.Caption, Theme.Muted, 14, TextAnchor.MiddleRight));
                y += 38;
            }

            // Status: connecting, or towed at the back.
            string status = !_group.IsConnected ? L.Get("connecting")
                : hasMe && (me.Flags & RiderFlags.Towed) != 0 && _group.Mode == LobbyMode.KeepTogether ? L.Get("towed")
                : null;
            if (status != null)
            {
                var style = Theme.With(Theme.Caption, Theme.Background, 15, TextAnchor.MiddleCenter);
                float w = style.CalcSize(new GUIContent(status)).x + 32;
                var chip = new Rect(panel.x, panel.yMax + 10, Mathf.Max(panel.width, w), 34);
                Ui.Rect(chip, _group.IsConnected ? Theme.Info : Theme.Warning, 17);
                GUI.Label(chip, status, style);
            }

            // Emoji buttons (bottom left, above the route / workout strip).
            float bx = 24, by = _h - 136 - 70;
            foreach (var (emoji, icon, color) in Emojis)
            {
                var b = new Rect(bx, by, 56, 56);
                Ui.Rect(b, Theme.Alpha(Theme.Background, 0.85f), 28);
                Ui.DrawIcon(new Rect(b.center.x - 14, b.center.y - 14, 28, 28), icon, color);
                if (GUI.Button(b, GUIContent.none, GUIStyle.none)) _group.SendEmoji(emoji);
                bx += 64;
            }

            // Own emojis rise from the buttons, so the rider sees they were sent.
            if (hasMe)
            {
                foreach (var e in recent.Where(e => e.Slot == _group.State.MySlot))
                {
                    float age = (float)(now - e.ReceivedS);
                    int index = System.Array.FindIndex(Emojis, x => x.Emoji == e.Emoji);
                    EmojiBubble(new Rect(24 + (Mathf.Max(0, index) * 64) + 4, by - 56 - (age * 30f), 48, 48), e.Emoji);
                }
            }
        }

        private static void EmojiBubble(Rect r, Emoji emoji)
        {
            var e = Emojis.FirstOrDefault(x => x.Emoji == emoji);
            Ui.Rect(r, Theme.Alpha(Theme.Surface, 0.95f), r.height / 2);
            Ui.DrawIcon(new Rect(r.center.x - (r.width * 0.3f), r.center.y - (r.height * 0.3f), r.width * 0.6f, r.height * 0.6f), e.Icon, e.Color);
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Text field with a grey placeholder while empty.</summary>
        private static string Field(Rect r, string text, int maxLength, string placeholder)
        {
            string value = Ui.TextField(r, text, maxLength);
            if (string.IsNullOrEmpty(value)) GUI.Label(new Rect(r.x + 16, r.y, r.width - 32, r.height), placeholder, Theme.With(Theme.Body, Theme.Faint, anchor: TextAnchor.MiddleLeft));
            return value;
        }

        /// <summary>"freevelo.example.org" → "https://freevelo.example.org" (without a trailing slash).</summary>
        private static string NormalizeServer(string input)
        {
            string s = (input ?? string.Empty).Trim().TrimEnd('/');
            if (s.Length > 0 && !s.StartsWith("http://", System.StringComparison.OrdinalIgnoreCase) && !s.StartsWith("https://", System.StringComparison.OrdinalIgnoreCase))
            {
                s = "https://" + s;
            }

            return s;
        }
    }
}

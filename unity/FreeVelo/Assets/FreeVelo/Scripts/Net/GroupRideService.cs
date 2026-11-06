using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using FreeVelo.App;
using FreeVelo.Shared.Api;
using FreeVelo.Shared.Live;
using FreeVelo.UI;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace FreeVelo.Net
{
    /// <summary>JSON for the API with Unity's JsonUtility (the messages are [Serializable] with public fields).</summary>
    public sealed class UnityJson : IJsonCodec
    {
        public string Serialize(object value) => JsonUtility.ToJson(value);

        public T Deserialize<T>(string json) => JsonUtility.FromJson<T>(json);
    }

    /// <summary>
    /// Group rides in the app: the rider's identity (display name only), the current group ride and its live
    /// connection. UI calls start operations and read <see cref="Busy"/> / <see cref="Lobby"/>; the ride logic calls
    /// <see cref="Publish"/> and <see cref="TryGetOwnPosition"/> – also from the background ticks, so those two are
    /// thread-safe. Reconnects automatically while a group ride is running.
    /// </summary>
    public sealed class GroupRideService : MonoBehaviour
    {
        private const float PublishIntervalS = 0.25f;
        private static readonly Stopwatch Clock = Stopwatch.StartNew();

        private FreeVeloApi _api;
        private string _apiServer;
        private LiveLink _link;
        private bool _wantLive;
        private float _nextConnectAt;
        private float _nextLobbyRefreshAt;
        private double _lastPublishS = double.NegativeInfinity;
        private int _handledWelcomes;

        /// <summary>Group state from the server (riders, positions, emojis).</summary>
        public GroupRideState State { get; } = new GroupRideState();

        /// <summary>The group ride the rider is in (null = none).</summary>
        public LobbyDetails Lobby { get; private set; }

        public LobbySummary[] PublicLobbies { get; private set; } = new LobbySummary[0];

        /// <summary>Friend requests waiting for an answer.</summary>
        public List<IncomingFriendRequest> FriendRequests { get; } = new List<IncomingFriendRequest>();

        /// <summary>Riders asked for friendship in this session (their "+" turns into "asked").</summary>
        public HashSet<string> AskedRiders { get; } = new HashSet<string>();

        private float _nextInboxAt;

        /// <summary>A request is running (buttons show a spinner state).</summary>
        public bool Busy { get; private set; }

        public bool HasIdentity => AppSettings.Rider != null;

        public bool HasServer => !string.IsNullOrEmpty(AppSettings.ServerUrl);

        /// <summary>A group ride is being ridden right now (the live connection is wanted).</summary>
        public bool IsLive => _wantLive;

        public bool IsConnected => _link != null && _link.IsOpen;

        public LobbyMode Mode => Lobby?.mode ?? LobbyMode.Free;

        /// <summary>The app's clock for live data (seconds; safe from any thread).</summary>
        public static double NowS => Clock.Elapsed.TotalSeconds;

        // ------------------------------------------------------------------ identity

        public async void CreateIdentity(string displayName)
        {
            await Run(async api =>
            {
                var credentials = await api.CreateRiderAsync(displayName.Trim());
                AppSettings.Rider = credentials;
            });
        }

        public async void Rename(string displayName)
        {
            await Run(async api =>
            {
                var info = await api.RenameAsync(displayName.Trim());
                var rider = AppSettings.Rider;
                rider.displayName = info.displayName;
                AppSettings.Rider = rider;
                Ui.Toast("✓ " + L.Get("saved"));
            });
        }

        // ------------------------------------------------------------------ group rides

        /// <summary>Public rides and the private rides of friends.</summary>
        public async void RefreshPublicLobbies()
        {
            await Run(async api => PublicLobbies = (await api.VisibleLobbiesAsync(FriendCodes())).lobbies ?? new LobbySummary[0], quiet: true);
        }

        private static string[] FriendCodes() => AppSettings.Friends.Select(f => f.code).ToArray();

        // ------------------------------------------------------------------ friends

        public async void RequestFriend(string riderId, string name)
        {
            await Run(async api =>
            {
                await api.RequestFriendAsync(riderId);
                AskedRiders.Add(riderId);
                Ui.Toast("✓ " + string.Format(L.Get("friend_asked"), name));
            });
        }

        public async void AcceptFriend(IncomingFriendRequest request)
        {
            FriendRequests.Remove(request);
            await Run(async api =>
            {
                var friend = await api.AcceptFriendAsync(request.id);
                AppSettings.AddFriend(friend);
                Ui.Toast("✓ " + string.Format(L.Get("friend_added"), friend.name));
            });
        }

        public async void DeclineFriend(IncomingFriendRequest request)
        {
            FriendRequests.Remove(request);
            await Run(async api => await api.DeclineFriendAsync(request.id), quiet: true);
        }

        /// <summary>New requests and accepted ones (every few seconds while the app is open).</summary>
        private async void CheckInbox()
        {
            await Run(async api =>
            {
                var inbox = await api.FriendInboxAsync();
                foreach (var request in inbox.requests ?? new IncomingFriendRequest[0])
                {
                    if (!FriendRequests.Any(r => r.id == request.id)) FriendRequests.Add(request);
                }

                foreach (var friend in inbox.accepted ?? new FriendInfo[0])
                {
                    AppSettings.AddFriend(friend);
                    Ui.Toast("✓ " + string.Format(L.Get("friend_added"), friend.name));
                }
            }, quiet: true);
        }

        /// <summary>A new friend code: friends lose access to private rides until they befriend again.</summary>
        public void ResetFriendCode()
        {
            AppSettings.ResetFriendCode();
            if (_api != null)
            {
                _api.FriendCode = AppSettings.FriendCode;
                _api.ResetSession();
            }
        }

        /// <summary>After an app start: back into the ride the rider was in (if it is still open).</summary>
        public async void RestoreLobby()
        {
            if (!HasIdentity || !HasServer) return;
            await Run(async api => Lobby = await api.CurrentLobbyAsync(), quiet: true);
        }

        public async void CreateLobby(CreateLobbyRequest request)
        {
            await Run(async api => Lobby = await api.CreateLobbyAsync(request));
        }

        public async void JoinByCode(string code)
        {
            await Run(async api => Lobby = await api.JoinByCodeAsync(code));
        }

        public async void JoinPublic(string lobbyId)
        {
            await Run(async api => Lobby = await api.JoinFromListAsync(lobbyId, FriendCodes()));
        }

        public async void Leave()
        {
            StopLive();
            await Run(async api => await api.LeaveLobbyAsync(), quiet: true);
            Lobby = null;
            State.Reset();
        }

        /// <summary>Text to send to friends (WhatsApp etc.).</summary>
        public string InviteText() => Lobby == null ? "" : string.Format(L.Get("invite_text"), Lobby.name, FormatCode(Lobby.code));

        /// <summary>"KPWD" → "KP WD" (easier to read out).</summary>
        public static string FormatCode(string code) => code != null && code.Length == 4 ? code.Substring(0, 2) + " " + code.Substring(2) : code ?? "";

        // ------------------------------------------------------------------ live

        /// <summary>The group ride starts: connect (and keep reconnecting) until <see cref="StopLive"/>.</summary>
        public void StartLive()
        {
            if (Lobby == null) return;
            _wantLive = true;
            _handledWelcomes = 0;
            _nextConnectAt = 0;
            State.Reset();
        }

        public void StopLive()
        {
            _wantLive = false;
            var link = _link;
            _link = null;
            if (link != null) _ = CloseQuietly(link);
        }

        /// <summary>
        /// Sends the rider's state (at most 4 times a second; any thread). <paramref name="distanceM"/> matters in Free
        /// mode, W/kg and cadence in Keep Together.
        /// </summary>
        public void Publish(double distanceM, double speedMps, double wkg, double cadenceRpm)
        {
            var link = _link;
            if (!_wantLive || link == null) return;
            double now = NowS;
            if (now - _lastPublishS < PublishIntervalS) return;
            _lastPublishS = now;
            link.Post(new RiderStateMessage
            {
                DistanceM = distanceM,
                LaneM = State.MyHomeLaneM,
                SpeedMps = (float)speedMps,
                Wkg = (float)wkg,
                CadenceRpm = (byte)Mathf.Clamp((float)cadenceRpm, 0, 255),
            });
        }

        public void SendEmoji(Emoji emoji) => _link?.Post(new EmojiMessage { Emoji = emoji });

        /// <summary>
        /// Keep Together: where the server puts this rider. <paramref name="welcomed"/> is true once per (re)connection,
        /// so the ride can jump there without counting the jump as distance.
        /// </summary>
        public bool TryGetOwnPosition(out double distanceM, out float speedMps, out bool welcomed)
        {
            distanceM = 0;
            speedMps = 0;
            welcomed = false;
            if (!_wantLive || !State.TryGetMine(NowS, out var mine)) return false;
            int welcomes = State.WelcomeCount;
            welcomed = welcomes != _handledWelcomes;
            _handledWelcomes = welcomes;
            distanceM = mine.DistanceM;
            speedMps = mine.SpeedMps;
            return true;
        }

        /// <summary>Free mode: where to start after a (re)connection (once), e.g. next to the group when joining late.</summary>
        public bool TryTakeStartDistance(out double distanceM)
        {
            distanceM = State.StartDistanceM;
            int welcomes = State.WelcomeCount;
            if (!_wantLive || welcomes == 0 || welcomes == _handledWelcomes) return false;
            bool first = _handledWelcomes == 0;
            _handledWelcomes = welcomes;
            return first; // after a reconnect the rider keeps their own position
        }

        private void Update()
        {
            if (HasIdentity && HasServer && Time.unscaledTime > _nextInboxAt)
            {
                _nextInboxAt = Time.unscaledTime + 5f;
                CheckInbox();
            }

            if (Lobby != null && !_wantLive && Time.unscaledTime > _nextLobbyRefreshAt)
            {
                // Waiting room: keep the member list fresh.
                _nextLobbyRefreshAt = Time.unscaledTime + 3f;
                RestoreLobby();
            }

            if (!_wantLive) return;

            var link = _link;
            if (link != null && !link.IsOpen)
            {
                _link = null;
                switch (link.CloseStatus)
                {
                    case LiveLink.LeftRide:
                    case LiveLink.RideClosed:
                    case LiveLink.NotInRide:
                        _wantLive = false;
                        Lobby = null;
                        Ui.Toast(L.Get("group_ended"));
                        return;
                    case LiveLink.Replaced:
                        _wantLive = false;
                        Ui.Toast(L.Get("group_other_device"));
                        return;
                }

                _nextConnectAt = Time.unscaledTime + 2f; // dropped: try again soon
            }

            if (_link == null && !_connecting && Time.unscaledTime >= _nextConnectAt) Connect();
        }

        private bool _connecting;

        private async void Connect()
        {
            _connecting = true;
            try
            {
                var api = Api();
                var socket = await api.ConnectLiveAsync();
                if (!_wantLive)
                {
                    socket.Dispose();
                    return;
                }

                _link = new LiveLink(socket, State, () => NowS);
            }
            catch (Exception e)
            {
                Debug.LogWarning("FreeVelo: live connection failed – " + e.Message);
                _nextConnectAt = Time.unscaledTime + 5f;
            }
            finally
            {
                _connecting = false;
            }
        }

        private void OnDestroy() => StopLive();

        private static async Task CloseQuietly(LiveLink link)
        {
            try
            {
                await link.CloseAsync();
            }
            finally
            {
                link.Dispose();
            }
        }

        // ------------------------------------------------------------------ plumbing

        private FreeVeloApi Api()
        {
            string server = AppSettings.ServerUrl;
            if (_api == null || _apiServer != server)
            {
                if (string.IsNullOrEmpty(server)) throw new InvalidOperationException(L.Get("no_server"));
                // Long timeout: a free host that went to sleep needs up to about a minute to wake up.
                var http = new HttpClient { BaseAddress = new Uri(server + "/"), Timeout = TimeSpan.FromSeconds(70) };
                _api = new FreeVeloApi(http, new UnityJson())
                {
                    // The server forgot the rider (free hosting without storage restarted): keep the new identity.
                    CredentialsReplaced = credentials => AppSettings.Rider = credentials,
                };
                _apiServer = server;
            }

            _api.Credentials = AppSettings.Rider;
            _api.FriendCode = AppSettings.FriendCode;
            return _api;
        }

        /// <summary>Runs a request; errors become a toast in the rider's language (unless <paramref name="quiet"/>).</summary>
        private async Task Run(Func<FreeVeloApi, Task> action, bool quiet = false)
        {
            if (Busy && !quiet) return;
            if (!quiet) Busy = true;
            try
            {
                await action(Api());
            }
            catch (ApiException e)
            {
                if (e.Status == System.Net.HttpStatusCode.Unauthorized && e.Code == ApiErrors.InvalidCredentials)
                {
                    // The server no longer knows this rider (e.g. a new server): start over with the name.
                    AppSettings.Rider = null;
                }

                if (!quiet) Ui.Toast("✗ " + Explain(e));
            }
            catch (Exception e)
            {
                Debug.LogWarning("FreeVelo: server request failed – " + e);
                if (!quiet) Ui.Toast("✗ " + (e is InvalidOperationException ? e.Message : L.Get("server_unreachable")));
            }
            finally
            {
                if (!quiet) Busy = false;
            }
        }

        private static string Explain(ApiException e)
        {
            switch (e.Code)
            {
                case ApiErrors.InvalidDisplayName: return L.Get("err_name");
                case ApiErrors.LobbyNotFound: return L.Get("err_code");
                case ApiErrors.LobbyFull: return L.Get("err_full");
                case ApiErrors.InvalidLobby: return L.Get("err_lobby");
                case ApiErrors.TooManyRequests: return L.Get("err_too_many");
                default: return L.Get("server_error") + $" ({(int)e.Status})";
            }
        }
    }
}

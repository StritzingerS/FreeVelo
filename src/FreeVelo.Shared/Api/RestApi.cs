using System;

namespace FreeVelo.Shared.Api
{
    // REST messages between the app and the server (JSON, camelCase).
    // Public fields instead of properties and [Serializable], so Unity's JsonUtility can read and write them too.

    public static class ApiRoutes
    {
        public const string Riders = "/api/v1/riders";
        public const string Session = "/api/v1/session";
        public const string Me = "/api/v1/me";
        public const string Lobbies = "/api/v1/lobbies";
        public const string CurrentLobby = "/api/v1/lobbies/current";
        public const string JoinByCode = "/api/v1/lobbies/join";
        public const string LeaveLobby = "/api/v1/lobbies/leave";

        /// <summary>WebSocket of the current group ride (binary live messages, see FreeVelo.Shared.Live).</summary>
        public const string Live = "/api/v1/live";

        /// <summary>Join a public group ride from the list: POST to this with the lobby id.</summary>
        public static string JoinPublic(string lobbyId) => $"/api/v1/lobbies/{lobbyId}/join";
    }

    /// <summary>Machine-readable error codes in <see cref="ApiError.error"/>.</summary>
    public static class ApiErrors
    {
        public const string InvalidDisplayName = "invalid_display_name";
        public const string InvalidCredentials = "invalid_credentials";
        public const string InvalidLobby = "invalid_lobby";
        public const string LobbyNotFound = "lobby_not_found";
        public const string LobbyFull = "lobby_full";
        public const string TooManyRequests = "too_many_requests";
    }

    public static class RiderRules
    {
        public const int MinDisplayNameLength = 2;
        public const int MaxDisplayNameLength = 24;
    }

    [Serializable]
    public class ApiError
    {
        public string error = "";
    }

    // ------------------------------------------------------------------ riders (no email, no password)

    /// <summary>First start of the app: only a display name.</summary>
    [Serializable]
    public class CreateRiderRequest
    {
        public string displayName = "";
    }

    /// <summary>
    /// The rider's identity, stored on the device. The secret is shown only once; it is the rider's "password",
    /// but nobody has to remember or type it.
    /// </summary>
    [Serializable]
    public class RiderCredentials
    {
        public string riderId = "";
        public string secret = "";
        public string displayName = "";
    }

    [Serializable]
    public class SessionRequest
    {
        public string riderId = "";
        public string secret = "";
    }

    [Serializable]
    public class SessionResponse
    {
        public string accessToken = "";

        /// <summary>Lifetime in seconds; ask for a new session when it runs out (or on a 401).</summary>
        public int expiresIn;

        public RiderInfo rider = new RiderInfo();
    }

    [Serializable]
    public class RiderInfo
    {
        public string id = "";
        public string displayName = "";
    }

    [Serializable]
    public class UpdateRiderRequest
    {
        public string displayName = "";
    }

    // ------------------------------------------------------------------ group rides

    public enum LobbyMode
    {
        /// <summary>The group stays together; who pushes more W/kg rides a bit ahead.</summary>
        KeepTogether = 0,

        /// <summary>Everyone rides their own speed.</summary>
        Free = 1,
    }

    public static class LobbyRules
    {
        public const int MinNameLength = 2;
        public const int MaxNameLength = 40;
        public const int DefaultMaxRiders = 8;
        public const int MinRiders = 2;
        public const int MaxRiders = 20;
        public const int CodeLength = 4;

        /// <summary>Letters of invite codes: no I, L and O (too easy to mix up with 1 and 0).</summary>
        public const string CodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ";

        /// <summary>Upper case, without spaces and dashes, so "ab cd" works as well as "ABCD".</summary>
        public static string NormalizeCode(string? code)
        {
            if (code == null) return "";
            var chars = new System.Text.StringBuilder(code.Length);
            foreach (char c in code)
            {
                if (char.IsLetter(c)) chars.Append(char.ToUpperInvariant(c));
            }

            return chars.ToString();
        }
    }

    /// <summary>Fixed when the group ride is created.</summary>
    [Serializable]
    public class CreateLobbyRequest
    {
        public string name = "";
        public LobbyMode mode = LobbyMode.KeepTogether;

        /// <summary>Private rides are not listed; friends join with the 4-letter code.</summary>
        public bool isPrivate = true;

        public int maxRiders = LobbyRules.DefaultMaxRiders;

        /// <summary>Route to ride, or empty with a workout (then a built-in looping route is used).</summary>
        public string routeId = "";

        /// <summary>Workout proposed to everyone (each rider may pick another one or ride free).</summary>
        public string workoutId = "";

        public string workoutName = "";
    }

    [Serializable]
    public class JoinByCodeRequest
    {
        public string code = "";
    }

    /// <summary>A group ride as members see it (with the code to pass on).</summary>
    [Serializable]
    public class LobbyDetails
    {
        public string id = "";

        /// <summary>4-letter invite code (only for members and the creator).</summary>
        public string code = "";

        public string name = "";
        public LobbyMode mode;
        public bool isPrivate;
        public int maxRiders;
        public string routeId = "";
        public string workoutId = "";
        public string workoutName = "";
        public string hostRiderId = "";
        public RiderInfo[] riders = new RiderInfo[0];
    }

    /// <summary>A public group ride in the list.</summary>
    [Serializable]
    public class LobbySummary
    {
        public string id = "";
        public string name = "";
        public LobbyMode mode;
        public int maxRiders;
        public int riderCount;
        public string routeId = "";
        public string workoutName = "";
        public string hostName = "";
    }

    [Serializable]
    public class LobbyList
    {
        public LobbySummary[] lobbies = new LobbySummary[0];
    }
}

using FreeVelo.Shared.Api;

namespace FreeVelo.Server.Friends;

/// <summary>
/// Friends without storing them: the phones keep their friends (name + secret friend code). The server only
/// remembers, in memory, which friend code belongs to which rider right now (sent with every session), and passes
/// friend requests and acceptances between the apps. Pending requests are lost when the server restarts.
/// </summary>
public sealed class FriendService
{
    public static readonly TimeSpan RequestLifetime = TimeSpan.FromDays(1);

    private readonly object _sync = new();
    private readonly Dictionary<string, string> _codeOfRider = new();
    private readonly Dictionary<string, string> _riderOfCode = new();
    private readonly Dictionary<string, PendingRequest> _requests = new();
    private readonly Dictionary<string, List<FriendInfo>> _accepted = new();
    private readonly TimeProvider _time;

    public FriendService(TimeProvider time) => _time = time;

    /// <summary>A friend code is 16–64 URL-safe characters (the app makes 128 random bits).</summary>
    public static bool IsValidCode(string? code) =>
        code != null && code.Length >= 16 && code.Length <= 64 && code.All(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_');

    public void Register(string riderId, string? code)
    {
        if (!IsValidCode(code)) return;
        lock (_sync)
        {
            if (_codeOfRider.TryGetValue(riderId, out var old)) _riderOfCode.Remove(old);
            if (_riderOfCode.TryGetValue(code!, out var previousRider)) _codeOfRider.Remove(previousRider); // same phone, new rider id
            _codeOfRider[riderId] = code!;
            _riderOfCode[code!] = riderId;
        }
    }

    public string? CodeOf(string riderId)
    {
        lock (_sync) return _codeOfRider.TryGetValue(riderId, out var code) ? code : null;
    }

    public void Forget(string riderId)
    {
        lock (_sync)
        {
            if (_codeOfRider.Remove(riderId, out var code)) _riderOfCode.Remove(code);
            foreach (var id in _requests.Where(r => r.Value.From == riderId || r.Value.To == riderId).Select(r => r.Key).ToList()) _requests.Remove(id);
            _accepted.Remove(riderId);
        }
    }

    /// <summary>Asks <paramref name="to"/> for friendship; false if either rider has no friend code yet.</summary>
    public bool Request(string from, string fromName, string to)
    {
        lock (_sync)
        {
            Expire();
            if (from == to || !_codeOfRider.ContainsKey(from) || !_codeOfRider.ContainsKey(to)) return false;
            if (_requests.Values.Any(r => r.From == from && r.To == to)) return true; // already asked
            string id = Guid.NewGuid().ToString("N")[..12];
            _requests[id] = new PendingRequest(id, from, fromName, to, _time.GetUtcNow());
            return true;
        }
    }

    /// <summary>New requests for the rider, and the riders who accepted theirs (handed out once).</summary>
    public FriendInbox Inbox(string riderId)
    {
        lock (_sync)
        {
            Expire();
            var inbox = new FriendInbox
            {
                requests = _requests.Values.Where(r => r.To == riderId).OrderBy(r => r.At).Select(r => new IncomingFriendRequest { id = r.Id, fromName = r.FromName }).ToArray(),
            };
            if (_accepted.Remove(riderId, out var accepted)) inbox.accepted = accepted.ToArray();
            return inbox;
        }
    }

    /// <summary>
    /// Accepts a request: returns the requester (name + code) for the accepting app, and queues the accepter for the
    /// requester's app. Null if the request is unknown or not for this rider.
    /// </summary>
    public FriendInfo? Accept(string requestId, string riderId, string riderName)
    {
        lock (_sync)
        {
            if (!_requests.TryGetValue(requestId, out var request) || request.To != riderId) return null;
            _requests.Remove(requestId);
            if (!_codeOfRider.TryGetValue(request.From, out var fromCode) || !_codeOfRider.TryGetValue(riderId, out var ownCode)) return null;
            if (!_accepted.TryGetValue(request.From, out var list)) _accepted[request.From] = list = new List<FriendInfo>();
            list.Add(new FriendInfo { name = riderName, code = ownCode });
            return new FriendInfo { name = request.FromName, code = fromCode };
        }
    }

    public bool Decline(string requestId, string riderId)
    {
        lock (_sync) return _requests.TryGetValue(requestId, out var r) && r.To == riderId && _requests.Remove(requestId);
    }

    /// <summary>Riders (ids) whose friend codes are among <paramref name="codes"/>.</summary>
    public HashSet<string> RidersOf(IEnumerable<string> codes)
    {
        lock (_sync)
        {
            var result = new HashSet<string>();
            foreach (var code in codes)
            {
                if (code != null && _riderOfCode.TryGetValue(code, out var rider)) result.Add(rider);
            }

            return result;
        }
    }

    private void Expire()
    {
        var now = _time.GetUtcNow();
        foreach (var id in _requests.Where(r => now - r.Value.At > RequestLifetime).Select(r => r.Key).ToList()) _requests.Remove(id);
    }

    private sealed record PendingRequest(string Id, string From, string FromName, string To, DateTimeOffset At);
}

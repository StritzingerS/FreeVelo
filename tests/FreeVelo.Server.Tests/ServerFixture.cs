using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace FreeVelo.Server.Tests;

/// <summary>A clock the tests can move forward.</summary>
public sealed class ManualTimeProvider : TimeProvider
{
    // Starts at the real time: access tokens are checked by the JWT middleware against the system clock.
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}

/// <summary>The real server in memory, with its own temporary data folder and a clock the tests control.</summary>
public sealed class ServerFactory : WebApplicationFactory<Program>
{
    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), "freevelo-test-" + Guid.NewGuid().ToString("N"));

    private readonly int _identityPerMinute;
    private readonly int _codeJoinsPerMinute;
    private readonly bool _behindProxy;

    public ServerFactory(int identityPerMinute = 1000, int codeJoinsPerMinute = 1000, bool behindProxy = false)
    {
        _identityPerMinute = identityPerMinute;
        _codeJoinsPerMinute = codeJoinsPerMinute;
        _behindProxy = behindProxy;
    }

    public ManualTimeProvider Time { get; } = new();

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { IncludeFields = true };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Server:DataDirectory", _dataDirectory);
        builder.UseSetting("RateLimit:IdentityRequestsPerMinute", _identityPerMinute.ToString());
        builder.UseSetting("RateLimit:CodeJoinsPerMinute", _codeJoinsPerMinute.ToString());
        builder.UseSetting("Server:BehindProxy", _behindProxy ? "true" : "false");
        builder.UseSetting("Live:TickMs", "50"); // 20 Hz instead of 4 Hz: live tests finish quickly
        builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(Time));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try
        {
            Directory.Delete(_dataDirectory, true);
        }
        catch (IOException)
        {
            // SQLite may still hold the file for a moment; it is a temp folder.
        }
    }
}

public static class HttpExtensions
{
    public static Task<HttpResponseMessage> Post<T>(this HttpClient client, string url, T body) =>
        client.PostAsJsonAsync(url, body, ServerFactory.Json);

    public static async Task<T> Read<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(ServerFactory.Json))!;
}

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FreeVelo.Server.Email;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace FreeVelo.Server.Tests;

/// <summary>Collects sent emails instead of sending them.</summary>
public sealed class CapturingEmailSender : IEmailSender
{
    private readonly List<EmailMessage> _sent = new();

    public IReadOnlyList<EmailMessage> Sent
    {
        get
        {
            lock (_sent) return _sent.ToList();
        }
    }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        lock (_sent) _sent.Add(message);
        return Task.CompletedTask;
    }

    /// <summary>Token from the last link to <paramref name="to"/> that points at <paramref name="path"/>.</summary>
    public string LastToken(string to, string path)
    {
        var mail = Sent.Last(m => m.To == to && m.Body.Contains(path + "?token="));
        return Uri.UnescapeDataString(Regex.Match(mail.Body, Regex.Escape(path) + @"\?token=([^\s]+)").Groups[1].Value);
    }
}

/// <summary>A clock the tests can move forward.</summary>
public sealed class ManualTimeProvider : TimeProvider
{
    // Starts at the real time: access tokens are checked by the JWT middleware against the system clock.
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}

/// <summary>The real server in memory, with its own temporary data folder, fast Argon2 and captured emails.</summary>
public sealed class ServerFactory : WebApplicationFactory<Program>
{
    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), "freevelo-test-" + Guid.NewGuid().ToString("N"));

    public ServerFactory(int authRequestsPerMinute = 1000) => AuthRequestsPerMinute = authRequestsPerMinute;

    public int AuthRequestsPerMinute { get; }

    public CapturingEmailSender Emails { get; } = new();

    public ManualTimeProvider Time { get; } = new();

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { IncludeFields = true };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Server:DataDirectory", _dataDirectory);
        builder.UseSetting("Server:PublicBaseUrl", "https://freevelo.test");
        builder.UseSetting("Argon2:MemoryKiB", "1024");
        builder.UseSetting("Argon2:Iterations", "1");
        builder.UseSetting("RateLimit:AuthRequestsPerMinute", AuthRequestsPerMinute.ToString());
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IEmailSender>(Emails);
            services.AddSingleton<TimeProvider>(Time);
        });
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

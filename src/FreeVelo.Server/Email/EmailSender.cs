using System.Net;
using System.Net.Mail;

namespace FreeVelo.Server.Email;

public sealed record EmailMessage(string To, string Subject, string Body);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

public sealed class SmtpOptions
{
    /// <summary>Empty: emails are only written to the log (development).</summary>
    public string Host { get; set; } = "";

    public int Port { get; set; } = 587;

    public string User { get; set; } = "";

    public string Password { get; set; } = "";

    public string From { get; set; } = "FreeVelo <noreply@example.com>";
}

/// <summary>Sends through an SMTP relay with STARTTLS (e.g. Brevo's free tier).</summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly SmtpOptions _options;

    public SmtpEmailSender(SmtpOptions options) => _options = options;

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(_options.User, _options.Password),
        };
        using var mail = new MailMessage
        {
            From = new MailAddress(_options.From),
            Subject = message.Subject,
            Body = message.Body,
        };
        mail.To.Add(message.To);
        await client.SendMailAsync(mail, cancellationToken);
    }
}

/// <summary>Development: the email (with its link) goes to the log instead of a mailbox.</summary>
public sealed class LogEmailSender : IEmailSender
{
    private readonly ILogger<LogEmailSender> _log;

    public LogEmailSender(ILogger<LogEmailSender> log) => _log = log;

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        _log.LogInformation("Email to {To}: {Subject}\n{Body}", message.To, message.Subject, message.Body);
        return Task.CompletedTask;
    }
}

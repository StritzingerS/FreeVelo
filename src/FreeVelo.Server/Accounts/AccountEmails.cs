using FreeVelo.Server.Email;

namespace FreeVelo.Server.Accounts;

/// <summary>Account emails in German or English (the app sends its language).</summary>
public static class AccountEmails
{
    public static EmailMessage Verify(string to, string name, string link, string? language) => IsGerman(language)
        ? new EmailMessage(to, "FreeVelo: E-Mail-Adresse bestätigen",
            $"Hallo {name},\n\nbitte bestätige deine E-Mail-Adresse, damit du dich in FreeVelo anmelden kannst:\n\n{link}\n\nDer Link ist 48 Stunden gültig. Wenn du dich nicht registriert hast, ignoriere diese E-Mail.\n\nViel Spaß beim Fahren!\nFreeVelo")
        : new EmailMessage(to, "FreeVelo: confirm your email address",
            $"Hi {name},\n\nplease confirm your email address so you can sign in to FreeVelo:\n\n{link}\n\nThe link is valid for 48 hours. If you did not sign up, ignore this email.\n\nEnjoy the ride!\nFreeVelo");

    /// <summary>Someone tried to register an existing address (the API does not reveal that, the owner learns it here).</summary>
    public static EmailMessage AlreadyRegistered(string to, string name, string? language) => IsGerman(language)
        ? new EmailMessage(to, "FreeVelo: du hast bereits ein Konto",
            $"Hallo {name},\n\nfür diese E-Mail-Adresse gibt es schon ein FreeVelo-Konto. Melde dich einfach an – oder setze in der App dein Passwort zurück, falls du es vergessen hast.\n\nFreeVelo")
        : new EmailMessage(to, "FreeVelo: you already have an account",
            $"Hi {name},\n\nthere already is a FreeVelo account for this email address. Just sign in – or reset your password in the app if you forgot it.\n\nFreeVelo");

    public static EmailMessage ResetPassword(string to, string name, string link, string? language) => IsGerman(language)
        ? new EmailMessage(to, "FreeVelo: Passwort zurücksetzen",
            $"Hallo {name},\n\nhier kannst du ein neues Passwort für FreeVelo festlegen:\n\n{link}\n\nDer Link ist 1 Stunde gültig. Wenn du das nicht angefordert hast, ignoriere diese E-Mail – dein Passwort bleibt unverändert.\n\nFreeVelo")
        : new EmailMessage(to, "FreeVelo: reset your password",
            $"Hi {name},\n\nset a new password for FreeVelo here:\n\n{link}\n\nThe link is valid for 1 hour. If you did not ask for this, ignore this email – your password stays unchanged.\n\nFreeVelo");

    public static bool IsGerman(string? language) => language != null && language.StartsWith("de", StringComparison.OrdinalIgnoreCase);
}

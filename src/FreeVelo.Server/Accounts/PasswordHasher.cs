using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace FreeVelo.Server.Accounts;

/// <summary>Argon2id cost parameters (OWASP minimum: 19 MiB, 2 iterations, 1 lane).</summary>
public sealed class Argon2Options
{
    public int MemoryKiB { get; set; } = 19 * 1024;
    public int Iterations { get; set; } = 2;
    public int Parallelism { get; set; } = 1;
}

/// <summary>
/// Argon2id password hashes in the PHC string format
/// (<c>$argon2id$v=19$m=19456,t=2,p=1$salt$hash</c>), so the cost can be raised later without breaking old hashes.
/// </summary>
public sealed class PasswordHasher
{
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private readonly Argon2Options _options;

    public PasswordHasher(Argon2Options options) => _options = options;

    public string Hash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
        byte[] hash = Compute(password, salt, _options.MemoryKiB, _options.Iterations, _options.Parallelism, HashBytes);
        return $"$argon2id$v=19$m={_options.MemoryKiB},t={_options.Iterations},p={_options.Parallelism}" +
               $"${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string encoded)
    {
        var parts = encoded.Split('$');
        // "", "argon2id", "v=19", "m=..,t=..,p=..", salt, hash
        if (parts.Length != 6 || parts[1] != "argon2id") return false;
        int m = 0, t = 0, p = 0;
        foreach (var kv in parts[3].Split(','))
        {
            var pair = kv.Split('=');
            if (pair.Length != 2 || !int.TryParse(pair[1], out int value)) return false;
            switch (pair[0])
            {
                case "m": m = value; break;
                case "t": t = value; break;
                case "p": p = value; break;
            }
        }

        if (m <= 0 || t <= 0 || p <= 0) return false;
        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[4]);
            expected = Convert.FromBase64String(parts[5]);
        }
        catch (FormatException)
        {
            return false;
        }

        byte[] actual = Compute(password, salt, m, t, p, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>True when the hash was made with weaker parameters than the current ones (rehash on login).</summary>
    public bool NeedsRehash(string encoded) =>
        !encoded.Contains($"m={_options.MemoryKiB},t={_options.Iterations},p={_options.Parallelism}$", StringComparison.Ordinal);

    private static byte[] Compute(string password, byte[] salt, int memoryKiB, int iterations, int parallelism, int length)
    {
        using var argon = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memoryKiB,
            Iterations = iterations,
            DegreeOfParallelism = parallelism,
        };
        return argon.GetBytes(length);
    }
}

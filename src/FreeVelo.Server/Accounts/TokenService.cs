using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace FreeVelo.Server.Accounts;

public sealed class JwtOptions
{
    public const string Issuer = "freevelo";
    public const string Audience = "freevelo-app";

    /// <summary>HMAC key (base64, at least 32 bytes). Empty: a key is generated once and kept in the data folder.</summary>
    public string SigningKey { get; set; } = "";

    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Long, so solo rides work offline for weeks after one login.</summary>
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(90);
}

/// <summary>Short-lived JWT access tokens (HMAC-SHA256); the account id is the subject.</summary>
public sealed class TokenService
{
    private readonly JwtOptions _options;
    private readonly TimeProvider _time;

    public TokenService(JwtOptions options, SymmetricSecurityKey key, TimeProvider time)
    {
        _options = options;
        Key = key;
        _time = time;
    }

    public SymmetricSecurityKey Key { get; }

    public string CreateAccessToken(Account account)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var token = new JwtSecurityToken(
            JwtOptions.Issuer,
            JwtOptions.Audience,
            new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, account.Id),
                new Claim("name", account.DisplayName),
            },
            notBefore: now,
            expires: now + _options.AccessTokenLifetime,
            signingCredentials: new SigningCredentials(Key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>The configured key, or one generated and stored in <paramref name="dataDirectory"/> on first start.</summary>
    public static SymmetricSecurityKey LoadKey(JwtOptions options, string dataDirectory)
    {
        if (!string.IsNullOrWhiteSpace(options.SigningKey))
        {
            byte[] configured = Convert.FromBase64String(options.SigningKey);
            if (configured.Length < 32) throw new InvalidOperationException("Jwt:SigningKey must be at least 32 bytes (base64).");
            return new SymmetricSecurityKey(configured);
        }

        string path = Path.Combine(dataDirectory, "jwt-signing.key");
        if (!File.Exists(path))
        {
            File.WriteAllText(path, Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)));
        }

        return new SymmetricSecurityKey(Convert.FromBase64String(File.ReadAllText(path).Trim()));
    }
}

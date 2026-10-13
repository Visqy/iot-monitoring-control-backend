using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using IotBackend.Contracts;
using IotBackend.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace IotBackend.Services;

public sealed class AuthService
{
    private static readonly object PasswordHasherUser = new();

    private readonly AuthOptions _options;
    private readonly PasswordHasher<object> _passwordHasher = new();

    public AuthService(IOptions<AuthOptions> options)
    {
        _options = options.Value;
    }

    public static string HashPassword(string password) =>
        new PasswordHasher<object>().HashPassword(PasswordHasherUser, password);

    public LoginResponse? Login(string username, string password)
    {
        if (!string.Equals(username, _options.Username, StringComparison.Ordinal))
        {
            return null;
        }

        var verifyResult = _passwordHasher.VerifyHashedPassword(PasswordHasherUser, _options.PasswordHash, password);
        if (verifyResult == PasswordVerificationResult.Failed)
        {
            return null;
        }

        var expiresAt = DateTimeOffset.UtcNow.AddHours(_options.TokenLifetimeHours);
        var token = GenerateToken(username, expiresAt);

        return new LoginResponse { Token = token, ExpiresAt = expiresAt };
    }

    private string GenerateToken(string username, DateTimeOffset expiresAt)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.JwtSigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[] { new Claim(ClaimTypes.Name, username) };

        var token = new JwtSecurityToken(
            claims: claims,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

using System.IdentityModel.Tokens.Jwt;
using IotBackend.Infrastructure;
using IotBackend.Options;
using IotBackend.Services;
using Xunit;

namespace IotBackend.Tests.Unit;

public class AuthServiceTests
{
    private static AuthService CreateService() => new(Microsoft.Extensions.Options.Options.Create(new AuthOptions
    {
        Username = "admin",
        PasswordHash = AuthService.HashPassword("correct-horse"),
        JwtSigningKey = "0123456789-0123456789-0123456789-ab",
        TokenLifetimeHours = 2
    }));

    [Fact]
    public void Login_WithCorrectCredentials_ReturnsTokenWithExpectedLifetime()
    {
        var before = DateTimeOffset.UtcNow;

        var result = CreateService().Login("admin", "correct-horse");

        Assert.NotNull(result);
        Assert.InRange(result!.ExpiresAt, before.AddHours(2).AddSeconds(-5), DateTimeOffset.UtcNow.AddHours(2).AddSeconds(5));
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);
        Assert.Equal("HS256", jwt.Header.Alg);
    }

    [Theory]
    [InlineData("admin", "wrong")]
    [InlineData("other", "correct-horse")]
    [InlineData("", "")]
    public void Login_WithWrongCredentials_ReturnsNull(string username, string password)
    {
        Assert.Null(CreateService().Login(username, password));
    }

    [Fact]
    public void PasswordHashCommand_PrintsAHashThatVerifies()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = PasswordHashCommand.Run("correct-horse", output, error);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        Assert.True(StartupConfigurationValidator.LooksLikeIdentityPasswordHash(output.ToString().Trim()));
    }

    [Fact]
    public void PasswordHashCommand_RejectsEmptyPassword()
    {
        var error = new StringWriter();

        Assert.Equal(1, PasswordHashCommand.Run("", new StringWriter(), error));
        Assert.Contains("No password", error.ToString());
    }
}

using IotBackend.Infrastructure;
using IotBackend.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace IotBackend.Tests.Unit;

public class ConfigurationValidationTests
{
    private const string GoodKey = "0123456789-0123456789-0123456789-ab";

    private static Dictionary<string, string?> ValidSettings() => new()
    {
        ["ConnectionStrings:Postgres"] = "Host=localhost;Database=x;Username=u;Password=p",
        ["Mqtt:Host"] = "broker.example.test",
        ["Mqtt:Port"] = "8883",
        ["Auth:Username"] = "admin",
        ["Auth:PasswordHash"] = AuthService.HashPassword("a-test-password"),
        ["Auth:JwtSigningKey"] = GoodKey,
        ["Auth:TokenLifetimeHours"] = "12"
    };

    private static IReadOnlyList<string> Validate(Dictionary<string, string?> settings) =>
        StartupConfigurationValidator.Validate(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());

    [Fact]
    public void CompleteConfiguration_HasNoErrors()
    {
        Assert.Empty(Validate(ValidSettings()));
    }

    [Theory]
    [InlineData("ConnectionStrings:Postgres")]
    [InlineData("Mqtt:Host")]
    [InlineData("Auth:Username")]
    [InlineData("Auth:PasswordHash")]
    [InlineData("Auth:JwtSigningKey")]
    public void MissingRequiredSetting_IsReportedByName(string key)
    {
        var settings = ValidSettings();
        settings[key] = "";

        var errors = Validate(settings);

        Assert.Contains(errors, e => e.Contains(key));
    }

    [Fact]
    public void ShortJwtSigningKey_IsRejected_WithoutEchoingTheKey()
    {
        const string shortKey = "short-secret-value";
        var settings = ValidSettings();
        settings["Auth:JwtSigningKey"] = shortKey;

        var errors = Validate(settings);

        Assert.Contains(errors, e => e.Contains("too short"));
        Assert.DoesNotContain(errors, e => e.Contains(shortKey));
    }

    [Fact]
    public void PlaintextPasswordInPasswordHash_IsRejected_WithoutEchoingIt()
    {
        const string plaintext = "my-plain-password!";
        var settings = ValidSettings();
        settings["Auth:PasswordHash"] = plaintext;

        var errors = Validate(settings);

        Assert.Contains(errors, e => e.Contains("hash-password"));
        Assert.DoesNotContain(errors, e => e.Contains(plaintext));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("70000")]
    public void MqttPortOutOfRange_IsRejected(string port)
    {
        var settings = ValidSettings();
        settings["Mqtt:Port"] = port;

        Assert.Contains(Validate(settings), e => e.Contains("Mqtt:Port"));
    }

    [Fact]
    public void NonPositiveTokenLifetime_IsRejected()
    {
        var settings = ValidSettings();
        settings["Auth:TokenLifetimeHours"] = "0";

        Assert.Contains(Validate(settings), e => e.Contains("TokenLifetimeHours"));
    }

    [Fact]
    public void ValidateOrThrow_ListsAllProblemsInOneException()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();

        var ex = Assert.Throws<InvalidOperationException>(() => StartupConfigurationValidator.ValidateOrThrow(configuration));

        Assert.Contains("ConnectionStrings:Postgres", ex.Message);
        Assert.Contains("Auth:JwtSigningKey", ex.Message);
        Assert.Contains("Auth:PasswordHash", ex.Message);
    }

    [Fact]
    public void GeneratedHash_IsAcceptedAsIdentityHash()
    {
        Assert.True(StartupConfigurationValidator.LooksLikeIdentityPasswordHash(AuthService.HashPassword("x")));
    }
}

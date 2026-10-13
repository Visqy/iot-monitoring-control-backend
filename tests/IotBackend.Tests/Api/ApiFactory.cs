using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using IotBackend.BackgroundServices;
using IotBackend.Infrastructure;
using IotBackend.Services;
using IotBackend.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace IotBackend.Tests.Api;

/// <summary>
/// Boots the real application in-process. Configuration is injected through environment
/// variables (restored on dispose) because Program.cs validates it eagerly at startup.
/// All values are generated for the test run; no real credentials are involved.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string Username = "admin";
    public const string Password = "test-password-123";
    public const string UnreachablePostgres =
        "Host=127.0.0.1;Port=1;Database=iot_unreachable_test;Username=u;Password=p;Timeout=1;Command Timeout=1;Pooling=false";

    private static readonly string SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

    private readonly Dictionary<string, string?> _previousEnvironment = new();
    private readonly bool _useRealMqtt;

    public ApiFactory(string postgresConnectionString, bool useRealMqtt = false, Dictionary<string, string>? extraSettings = null)
    {
        _useRealMqtt = useRealMqtt;

        var settings = new Dictionary<string, string>
        {
            ["ConnectionStrings__Postgres"] = postgresConnectionString,
            ["Mqtt__Host"] = "127.0.0.1",
            ["Mqtt__Port"] = "1883",
            ["Mqtt__UseTls"] = "false",
            ["Auth__Username"] = Username,
            ["Auth__PasswordHash"] = AuthService.HashPassword(Password),
            ["Auth__JwtSigningKey"] = SigningKey,
            ["Auth__TokenLifetimeHours"] = "1"
        };

        foreach (var (key, value) in extraSettings ?? new())
        {
            settings[key] = value;
        }

        foreach (var (key, value) in settings)
        {
            _previousEnvironment[key] = Environment.GetEnvironmentVariable(key);
            Environment.SetEnvironmentVariable(key, value);
        }
    }

    public FakeMqttPublisher Publisher { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            if (_useRealMqtt)
            {
                return;
            }

            var backgroundTypes = new[]
            {
                typeof(MqttSubscriberService),
                typeof(RelayCommandTimeoutService),
                typeof(DeviceOfflineSweepService)
            };
            var hosted = services
                .Where(d => d.ServiceType == typeof(IHostedService) && backgroundTypes.Contains(d.ImplementationType))
                .ToList();
            foreach (var descriptor in hosted)
            {
                services.Remove(descriptor);
            }

            services.RemoveAll<IMqttPublisher>();
            services.AddSingleton<IMqttPublisher>(Publisher);
        });
    }

    public async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username = Username, password = Password });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginBody>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.Token);
        return client;
    }

    public T GetScoped<T>(Func<IServiceProvider, T> resolve)
    {
        using var scope = Services.CreateScope();
        return resolve(scope.ServiceProvider);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
        {
            return;
        }

        foreach (var (key, value) in _previousEnvironment)
        {
            Environment.SetEnvironmentVariable(key, value);
        }
    }

    public sealed record LoginBody(string Token, DateTimeOffset ExpiresAt);
}

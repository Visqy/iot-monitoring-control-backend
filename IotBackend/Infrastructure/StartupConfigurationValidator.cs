using System.Text;

namespace IotBackend.Infrastructure;

public static class StartupConfigurationValidator
{
    public const int MinimumJwtSigningKeyBytes = 32;

    public static IReadOnlyList<string> Validate(IConfiguration configuration)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("Postgres")))
        {
            errors.Add("ConnectionStrings:Postgres is not set (env var ConnectionStrings__Postgres).");
        }

        if (string.IsNullOrWhiteSpace(configuration["Mqtt:Host"]))
        {
            errors.Add("Mqtt:Host is not set (env var Mqtt__Host). The MQTT client cannot be created without a broker host.");
        }

        var mqttPort = configuration.GetValue("Mqtt:Port", 8883);
        if (mqttPort is < 1 or > 65535)
        {
            errors.Add("Mqtt:Port must be between 1 and 65535.");
        }

        var signingKey = configuration["Auth:JwtSigningKey"];
        if (string.IsNullOrWhiteSpace(signingKey))
        {
            errors.Add("Auth:JwtSigningKey is not set (env var Auth__JwtSigningKey).");
        }
        else if (Encoding.UTF8.GetByteCount(signingKey) < MinimumJwtSigningKeyBytes)
        {
            errors.Add($"Auth:JwtSigningKey is too short; it must be at least {MinimumJwtSigningKeyBytes} bytes (256 bits) for HS256.");
        }

        if (string.IsNullOrWhiteSpace(configuration["Auth:Username"]))
        {
            errors.Add("Auth:Username is not set (env var Auth__Username).");
        }

        var passwordHash = configuration["Auth:PasswordHash"];
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            errors.Add("Auth:PasswordHash is not set (env var Auth__PasswordHash).");
        }
        else if (!LooksLikeIdentityPasswordHash(passwordHash))
        {
            errors.Add("Auth:PasswordHash is not a valid ASP.NET Core Identity password hash. " +
                       "Generate one with: dotnet run --no-launch-profile --project IotBackend -- hash-password");
        }

        if (configuration.GetValue("Auth:TokenLifetimeHours", 24) <= 0)
        {
            errors.Add("Auth:TokenLifetimeHours must be greater than zero.");
        }

        return errors;
    }

    public static void ValidateOrThrow(IConfiguration configuration)
    {
        var errors = Validate(configuration);
        if (errors.Count == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            "Invalid application configuration:" + Environment.NewLine +
            string.Join(Environment.NewLine, errors.Select(e => "  - " + e)) + Environment.NewLine +
            "See IotBackend/.env.example for the required variables.");
    }

    internal static bool LooksLikeIdentityPasswordHash(string value)
    {
        try
        {
            var bytes = Convert.FromBase64String(value);
            return bytes.Length > 13 && bytes[0] is 0x00 or 0x01;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

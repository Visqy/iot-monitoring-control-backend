using Dapper;
using IotBackend.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace IotBackend.Tests.Support;

/// <summary>
/// Gives tests access to a disposable PostgreSQL database named by the IOT_TEST_POSTGRES
/// environment variable. Tests that need it are skipped when the variable is not set.
/// </summary>
public static class TestDatabase
{
    public const string EnvironmentVariable = "IOT_TEST_POSTGRES";
    public const string SkipReason =
        "Set IOT_TEST_POSTGRES to the connection string of a disposable PostgreSQL database whose name contains 'test'.";

    private static readonly Lazy<NpgsqlDataSource?> LazyDataSource = new(Create);
    private static readonly SemaphoreSlim InitLock = new(1, 1);
    private static bool _schemaReady;

    static TestDatabase()
    {
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    public static string? ConnectionString => Environment.GetEnvironmentVariable(EnvironmentVariable);

    public static bool IsConfigured => !string.IsNullOrWhiteSpace(ConnectionString);

    public static NpgsqlDataSource DataSource =>
        LazyDataSource.Value ?? throw new InvalidOperationException(SkipReason);

    private static NpgsqlDataSource? Create()
    {
        if (!IsConfigured)
        {
            return null;
        }

        var database = new NpgsqlConnectionStringBuilder(ConnectionString).Database ?? string.Empty;
        if (!database.Contains("test", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Refusing to run destructive tests: the database name must contain 'test' ({EnvironmentVariable}).");
        }

        return NpgsqlDataSource.Create(ConnectionString!);
    }

    public static async Task ResetAsync()
    {
        await InitLock.WaitAsync();
        try
        {
            if (!_schemaReady)
            {
                await new DatabaseInitializer(DataSource, NullLogger<DatabaseInitializer>.Instance).InitializeAsync();
                _schemaReady = true;
            }

            await using var connection = await DataSource.OpenConnectionAsync();
            await connection.ExecuteAsync(
                "TRUNCATE telemetry, device_current_state, devices, relay_commands, rfid_cards, rfid_events RESTART IDENTITY");
        }
        finally
        {
            InitLock.Release();
        }
    }
}

public sealed class RequiresPostgresFactAttribute : FactAttribute
{
    public RequiresPostgresFactAttribute()
    {
        if (!TestDatabase.IsConfigured)
        {
            Skip = TestDatabase.SkipReason;
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DatabaseCollection
{
    public const string Name = "Database";
}

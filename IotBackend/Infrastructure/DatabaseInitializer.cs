using Npgsql;

namespace IotBackend.Infrastructure;

public sealed class DatabaseInitializer
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(NpgsqlDataSource dataSource, ILogger<DatabaseInitializer> logger)
    {
        _dataSource = dataSource;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var sqlPath = Path.Combine(AppContext.BaseDirectory, "Database", "001_initial_schema.sql");
        if (!File.Exists(sqlPath))
        {
            _logger.LogWarning("File schema tidak ditemukan di {Path}, lewati inisialisasi DB.", sqlPath);
            return;
        }

        try
        {
            var sql = await File.ReadAllTextAsync(sqlPath, cancellationToken);
            await using var command = _dataSource.CreateCommand(sql);
            await command.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Schema database terinisialisasi.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Inisialisasi schema database gagal. Aplikasi tetap jalan; /api/health akan melaporkan postgres=down.");
        }
    }
}

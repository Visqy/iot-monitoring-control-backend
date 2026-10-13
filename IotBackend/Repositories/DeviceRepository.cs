using Dapper;
using IotBackend.Models;
using Npgsql;

namespace IotBackend.Repositories;

public sealed class DeviceRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public DeviceRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    private const string ListSql = """
        SELECT d.device_id, d.name, d.location, COALESCE(dcs.status, 'unknown') AS status, d.updated_at
        FROM devices d
        LEFT JOIN device_current_state dcs ON dcs.device_id = d.device_id
        ORDER BY d.device_id
        """;

    public async Task<List<DeviceRecord>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<DeviceRecord>(new CommandDefinition(ListSql, cancellationToken: cancellationToken));
        return rows.AsList();
    }

    private const string EnsureRegisteredSql = """
        INSERT INTO devices (device_id, updated_at)
        VALUES (@device_id, NOW())
        ON CONFLICT (device_id) DO NOTHING
        """;

    public async Task EnsureRegisteredAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        await connection.ExecuteAsync(
            new CommandDefinition(EnsureRegisteredSql, new { device_id = deviceId }, cancellationToken: cancellationToken));
    }
}

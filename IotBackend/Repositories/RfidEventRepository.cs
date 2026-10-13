using Dapper;
using IotBackend.Models;
using Npgsql;

namespace IotBackend.Repositories;

public sealed class RfidEventRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public RfidEventRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    private const string InsertSql = """
        INSERT INTO rfid_events (device_id, uid, recognized, scanned_at, raw_payload)
        VALUES (@device_id, @uid, @recognized, @scanned_at, @raw_payload::jsonb)
        """;

    public async Task InsertAsync(
        string deviceId, string uid, bool recognized, DateTimeOffset? scannedAt, string rawPayloadJson,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var parameters = new
        {
            device_id = deviceId,
            uid,
            recognized,
            scanned_at = scannedAt,
            raw_payload = rawPayloadJson
        };

        await connection.ExecuteAsync(new CommandDefinition(InsertSql, parameters, cancellationToken: cancellationToken));
    }

    private const string GetHistorySql = """
        SELECT id, device_id, uid, recognized, scanned_at, received_at
        FROM rfid_events
        ORDER BY received_at DESC
        LIMIT @limit
        """;

    public async Task<List<RfidEventRecord>> GetHistoryAsync(int limit, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var rows = await connection.QueryAsync<RfidEventRecord>(
            new CommandDefinition(GetHistorySql, new { limit }, cancellationToken: cancellationToken));

        return rows.AsList();
    }
}

using Dapper;
using IotBackend.Models;
using Npgsql;

namespace IotBackend.Repositories;

public sealed class TelemetryRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public TelemetryRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    private const string InsertSql = """
        INSERT INTO telemetry
            (device_id, topic, voltage_b, current_b, power_b, energy_b, frequency_b, device_timestamp, raw_payload)
        VALUES
            (@device_id, @topic, @voltage_b, @current_b, @power_b, @energy_b, @frequency_b, @device_timestamp, @raw_payload::jsonb)
        """;

    public async Task InsertAsync(TelemetryRecord record, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var parameters = new
        {
            device_id = record.DeviceId,
            topic = record.Topic,
            voltage_b = record.VoltageB,
            current_b = record.CurrentB,
            power_b = record.PowerB,
            energy_b = record.EnergyB,
            frequency_b = record.FrequencyB,
            device_timestamp = record.DeviceTimestamp,
            raw_payload = record.RawPayload
        };

        await connection.ExecuteAsync(new CommandDefinition(InsertSql, parameters, cancellationToken: cancellationToken));
    }

    private const string GetHistorySql = """
        SELECT id, device_id, topic, voltage_b, current_b, power_b, energy_b, frequency_b, device_timestamp, received_at
        FROM telemetry
        WHERE device_id = @device_id
          AND (@from IS NULL OR received_at >= @from)
          AND (@to IS NULL OR received_at <= @to)
        ORDER BY received_at DESC
        LIMIT @limit
        """;

    public async Task<List<TelemetryHistoryRecord>> GetHistoryAsync(
        string deviceId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int limit,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var parameters = new { device_id = deviceId, from = from?.ToUniversalTime(), to = to?.ToUniversalTime(), limit };
        var rows = await connection.QueryAsync<TelemetryHistoryRecord>(
            new CommandDefinition(GetHistorySql, parameters, cancellationToken: cancellationToken));

        return rows.AsList();
    }
}

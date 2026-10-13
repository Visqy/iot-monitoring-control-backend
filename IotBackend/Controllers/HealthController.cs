using Dapper;
using IotBackend.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace IotBackend.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class HealthController : ControllerBase
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly MqttClientService _mqtt;
    private readonly ILogger<HealthController> _logger;

    public HealthController(NpgsqlDataSource dataSource, MqttClientService mqtt, ILogger<HealthController> logger)
    {
        _dataSource = dataSource;
        _mqtt = mqtt;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var (postgresOk, activeConnections, maxConnections) = await CheckPostgresAsync(cancellationToken);
        var mqttOk = _mqtt.IsConnected;
        var allOk = postgresOk && mqttOk;

        var payload = new
        {
            status = allOk ? "ok" : "degraded",
            application = "IotBackend",
            timestamp = DateTimeOffset.UtcNow,
            dependencies = new
            {
                postgres = postgresOk ? "up" : "down",
                mqtt = mqttOk ? "up" : "down"
            },
            postgresPool = postgresOk
                ? new { activeConnections, maxConnections }
                : null
        };

        return allOk
            ? Ok(payload)
            : StatusCode(StatusCodes.Status503ServiceUnavailable, payload);
    }

    private async Task<(bool Ok, int? ActiveConnections, int? MaxConnections)> CheckPostgresAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

            var activeConnections = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database()",
                cancellationToken: cancellationToken));

            var maxConnections = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT setting::int FROM pg_settings WHERE name = 'max_connections'",
                cancellationToken: cancellationToken));

            return (true, activeConnections, maxConnections);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Health check Postgres gagal.");
            return (false, null, null);
        }
    }
}

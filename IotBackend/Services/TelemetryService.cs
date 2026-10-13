using System.Globalization;
using System.Text.Json;
using IotBackend.Contracts;
using IotBackend.Infrastructure;
using IotBackend.Models;
using IotBackend.Repositories;

namespace IotBackend.Services;

public sealed class TelemetryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private const string DeviceTimestampFormat = "yyyy-MM-dd HH:mm:ss";

    private static readonly TimeSpan DeviceTimeZoneOffset = TimeSpan.FromHours(7);

    private readonly TelemetryRepository _telemetryRepository;
    private readonly DeviceStateRepository _deviceStateRepository;
    private readonly DeviceRepository _deviceRepository;
    private readonly RealtimeBroadcaster _broadcaster;
    private readonly ILogger<TelemetryService> _logger;

    public TelemetryService(
        TelemetryRepository telemetryRepository,
        DeviceStateRepository deviceStateRepository,
        DeviceRepository deviceRepository,
        RealtimeBroadcaster broadcaster,
        ILogger<TelemetryService> logger)
    {
        _telemetryRepository = telemetryRepository;
        _deviceStateRepository = deviceStateRepository;
        _deviceRepository = deviceRepository;
        _broadcaster = broadcaster;
        _logger = logger;
    }

    public async Task ProcessTelemetryAsync(
        string deviceId,
        string topic,
        string rawPayload,
        CancellationToken cancellationToken = default)
    {
        TelemetryPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<TelemetryPayload>(rawPayload, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex,
                "Payload telemetry dari {DeviceId} bukan JSON valid, diabaikan. Raw: {Raw}", deviceId, rawPayload);
            return;
        }

        if (payload is null)
        {
            _logger.LogWarning("Payload telemetry dari {DeviceId} kosong/null setelah deserialize, diabaikan.", deviceId);
            return;
        }

        var deviceTimestamp = ParseDeviceTimestamp(payload.Timestamp);
        var receivedAt = DateTimeOffset.UtcNow;

        var record = new TelemetryRecord
        {
            DeviceId = deviceId,
            Topic = topic,
            VoltageB = payload.VoltageB,
            CurrentB = payload.CurrentB,
            PowerB = payload.PowerB,
            EnergyB = payload.EnergyB,
            FrequencyB = payload.FreqB,
            DeviceTimestamp = deviceTimestamp,
            RawPayload = rawPayload
        };
        await _telemetryRepository.InsertAsync(record, cancellationToken);

        var state = new DeviceCurrentState
        {
            DeviceId = deviceId,
            Status = "online",
            VoltageB = payload.VoltageB,
            CurrentB = payload.CurrentB,
            PowerB = payload.PowerB,
            EnergyB = payload.EnergyB,
            FrequencyB = payload.FreqB,
            LastSeen = receivedAt
        };
        await _deviceStateRepository.UpsertFromTelemetryAsync(state, cancellationToken);

        await _deviceRepository.EnsureRegisteredAsync(deviceId, cancellationToken);

        await BroadcastDeviceStateAsync(deviceId, cancellationToken);

        _logger.LogInformation(
            "Telemetry {DeviceId} tersimpan (Vb={Vb} Ib={Ib} Pb={Pb} Eb={Eb} Fb={Fb}).",
            deviceId, payload.VoltageB, payload.CurrentB, payload.PowerB, payload.EnergyB, payload.FreqB);
    }

    public async Task<List<TelemetryHistoryResponse>> GetHistoryAsync(
        string deviceId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var records = await _telemetryRepository.GetHistoryAsync(deviceId, from, to, limit, cancellationToken);

        return records.Select(r => new TelemetryHistoryResponse
        {
            Id = r.Id,
            DeviceId = r.DeviceId,
            VoltageB = r.VoltageB,
            CurrentB = r.CurrentB,
            PowerB = r.PowerB,
            EnergyB = r.EnergyB,
            FrequencyB = r.FrequencyB,
            DeviceTimestamp = r.DeviceTimestamp,
            ReceivedAt = r.ReceivedAt
        }).ToList();
    }

    private async Task BroadcastDeviceStateAsync(string deviceId, CancellationToken cancellationToken)
    {
        var state = await _deviceStateRepository.GetByDeviceIdAsync(deviceId, cancellationToken);
        if (state is null)
        {
            return;
        }

        _broadcaster.Publish("device-state", new DeviceStateResponse
        {
            DeviceId = state.DeviceId,
            Status = state.Status,
            VoltageB = state.VoltageB,
            CurrentB = state.CurrentB,
            PowerB = state.PowerB,
            EnergyB = state.EnergyB,
            FrequencyB = state.FrequencyB,
            RelayState = state.RelayState,
            LastSeen = state.LastSeen
        });
    }

    internal static DateTime? ParseDeviceTimestamp(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (DateTime.TryParseExact(raw, DeviceTimestampFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsedLocal))
        {
            return DateTime.SpecifyKind(parsedLocal - DeviceTimeZoneOffset, DateTimeKind.Utc);
        }

        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces,
                out var parsedWithOffset))
        {
            return parsedWithOffset.UtcDateTime;
        }

        return null;
    }
}

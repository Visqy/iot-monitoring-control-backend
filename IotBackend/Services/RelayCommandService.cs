using System.Globalization;
using System.Text.Json;
using IotBackend.Contracts;
using IotBackend.Infrastructure;
using IotBackend.Models;
using IotBackend.Repositories;

namespace IotBackend.Services;

public sealed class RelayCommandService
{
    private const string CommandSource = "dashboard";

    private static readonly JsonSerializerOptions PublishJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly JsonSerializerOptions ParseJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly RelayCommandRepository _relayCommandRepository;
    private readonly DeviceStateRepository _deviceStateRepository;
    private readonly IMqttPublisher _mqtt;
    private readonly RealtimeBroadcaster _broadcaster;
    private readonly ILogger<RelayCommandService> _logger;

    public RelayCommandService(
        RelayCommandRepository relayCommandRepository,
        DeviceStateRepository deviceStateRepository,
        IMqttPublisher mqtt,
        RealtimeBroadcaster broadcaster,
        ILogger<RelayCommandService> logger)
    {
        _relayCommandRepository = relayCommandRepository;
        _deviceStateRepository = deviceStateRepository;
        _mqtt = mqtt;
        _broadcaster = broadcaster;
        _logger = logger;
    }

    public async Task<RelayCommandResponse> SendCommandAsync(
        string deviceId, bool requestedState, CancellationToken cancellationToken = default)
    {
        var commandId = Guid.NewGuid();

        await _relayCommandRepository.InsertPendingAsync(commandId, deviceId, requestedState, CommandSource, cancellationToken);

        var payload = new RelayCommandPayload
        {
            CommandId = commandId,
            State = ToMqttState(requestedState),
            Source = CommandSource,
            IssuedAt = DateTimeOffset.UtcNow
        };
        var payloadJson = JsonSerializer.Serialize(payload, PublishJsonOptions);

        string status;
        try
        {
            await _mqtt.PublishAsync($"{deviceId}/relay/set", payloadJson, cancellationToken: cancellationToken);
            await _relayCommandRepository.MarkSentAsync(commandId, payloadJson, cancellationToken);
            status = "sent";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gagal publish relay command {CommandId} ke device {DeviceId}.", commandId, deviceId);
            await _relayCommandRepository.MarkFailedAsync(commandId, ex.Message, cancellationToken);
            status = "failed";
        }

        return new RelayCommandResponse
        {
            CommandId = commandId,
            DeviceId = deviceId,
            RequestedState = requestedState,
            Status = status
        };
    }

    public async Task<RelayCommandStatusResponse?> GetCommandAsync(Guid commandId, CancellationToken cancellationToken = default)
    {
        var record = await _relayCommandRepository.GetByIdAsync(commandId, cancellationToken);
        if (record is null)
        {
            return null;
        }

        return new RelayCommandStatusResponse
        {
            CommandId = record.CommandId,
            DeviceId = record.DeviceId,
            RequestedState = record.RequestedState,
            ActualState = record.ActualState,
            Status = record.Status,
            RequestedAt = record.RequestedAt,
            SentAt = record.SentAt,
            AcknowledgedAt = record.AcknowledgedAt,
            ErrorMessage = record.ErrorMessage
        };
    }

    public async Task<List<RelayCommandHistoryItemResponse>> GetHistoryAsync(
        string deviceId, int limit, CancellationToken cancellationToken = default)
    {
        var records = await _relayCommandRepository.GetHistoryAsync(deviceId, limit, cancellationToken);

        return records.Select(r => new RelayCommandHistoryItemResponse
        {
            CommandId = r.CommandId,
            DeviceId = r.DeviceId,
            RequestedState = r.RequestedState,
            ActualState = r.ActualState,
            Source = r.Source,
            Status = r.Status,
            RequestedAt = r.RequestedAt,
            SentAt = r.SentAt,
            AcknowledgedAt = r.AcknowledgedAt,
            ErrorMessage = r.ErrorMessage
        }).ToList();
    }

    public async Task ProcessRelayStateAsync(string deviceId, string rawPayload, CancellationToken cancellationToken = default)
    {
        Guid? commandId = null;
        bool? actualState = null;
        string? source = null;
        string? executedAtRaw = null;
        var rawPayloadJson = rawPayload;

        try
        {
            var payload = JsonSerializer.Deserialize<RelayStatePayload>(rawPayload, ParseJsonOptions);
            if (payload is not null)
            {
                commandId = payload.CommandId;
                actualState = ParseMqttState(payload.State);
                source = payload.Source;
                executedAtRaw = payload.ExecutedAt;
            }
        }
        catch (JsonException)
        {
            actualState = ParseMqttState(rawPayload);
            rawPayloadJson = JsonSerializer.Serialize(rawPayload);
        }

        if (actualState is null)
        {
            _logger.LogWarning("Payload relay/state dari {DeviceId} tidak dikenali, diabaikan. Raw: {Raw}", deviceId, rawPayload);
            return;
        }

        if (commandId is { } id)
        {
            var rowsAffected = await _relayCommandRepository.MarkExecutedAsync(id, actualState.Value, cancellationToken);
            if (rowsAffected == 0)
            {
                _logger.LogWarning("relay/state dengan commandId {CommandId} tidak match relay_commands manapun.", id);
            }
        }
        else
        {
            var effectiveSource = source ?? "unknown";
            var syntheticCommandId = $"{effectiveSource}-{Guid.NewGuid():N}";
            var acknowledgedAt = ParseExecutedAt(executedAtRaw) ?? DateTimeOffset.UtcNow;

            await _relayCommandRepository.InsertExecutedAsync(
                syntheticCommandId, deviceId, actualState.Value, effectiveSource, acknowledgedAt, rawPayloadJson, cancellationToken);

            _logger.LogInformation(
                "relay/state dari {DeviceId} dipicu oleh {Source} (tanpa commandId) -> relay_commands {CommandId}.",
                deviceId, effectiveSource, syntheticCommandId);
        }

        var stateRowsAffected = await _deviceStateRepository.UpdateRelayStateAsync(deviceId, actualState.Value, cancellationToken);
        if (stateRowsAffected == 0)
        {
            _logger.LogWarning(
                "device_current_state untuk {DeviceId} belum ada (belum pernah kirim telemetry), relay_state tidak tersimpan.",
                deviceId);
            return;
        }

        await BroadcastDeviceStateAsync(deviceId, cancellationToken);
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

    internal static string ToMqttState(bool state) => state ? "ON" : "OFF";

    internal static bool? ParseMqttState(string? state) => state?.Trim().ToUpperInvariant() switch
    {
        "ON" => true,
        "OFF" => false,
        _ => null
    };

    internal static DateTimeOffset? ParseExecutedAt(string? raw) =>
        !string.IsNullOrWhiteSpace(raw) && DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed.ToUniversalTime()
            : null;
}

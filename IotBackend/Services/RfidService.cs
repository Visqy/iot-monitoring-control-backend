using System.Text.Json;
using IotBackend.Contracts;
using IotBackend.Infrastructure;
using IotBackend.Models;
using IotBackend.Options;
using IotBackend.Repositories;
using Microsoft.Extensions.Options;

namespace IotBackend.Services;

public sealed class RfidService
{
    private static readonly JsonSerializerOptions ParseJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly RfidCardRepository _cardRepository;
    private readonly RfidEventRepository _eventRepository;
    private readonly IMqttPublisher _mqtt;
    private readonly MqttOptions _mqttOptions;
    private readonly RealtimeBroadcaster _broadcaster;
    private readonly ILogger<RfidService> _logger;

    public RfidService(
        RfidCardRepository cardRepository,
        RfidEventRepository eventRepository,
        IMqttPublisher mqtt,
        IOptions<MqttOptions> mqttOptions,
        RealtimeBroadcaster broadcaster,
        ILogger<RfidService> logger)
    {
        _cardRepository = cardRepository;
        _eventRepository = eventRepository;
        _mqtt = mqtt;
        _mqttOptions = mqttOptions.Value;
        _broadcaster = broadcaster;
        _logger = logger;
    }

    public async Task ProcessScanAsync(string deviceId, string rawPayload, CancellationToken cancellationToken = default)
    {
        RfidScanPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<RfidScanPayload>(rawPayload, ParseJsonOptions);
        }
        catch (JsonException)
        {
            _logger.LogWarning("Payload rfid dari {DeviceId} bukan JSON valid, diabaikan. Raw: {Raw}", deviceId, rawPayload);
            return;
        }

        if (string.IsNullOrWhiteSpace(payload?.Uid))
        {
            _logger.LogWarning("Payload rfid dari {DeviceId} tidak punya uid, diabaikan. Raw: {Raw}", deviceId, rawPayload);
            return;
        }

        var uid = NormalizeUid(payload.Uid);
        await _eventRepository.InsertAsync(deviceId, uid, payload.Recognized, scannedAt: null, rawPayload, cancellationToken);

        _broadcaster.Publish("rfid-scan", new
        {
            deviceId,
            uid,
            recognized = payload.Recognized,
            scannedAt = (DateTimeOffset?)null,
            receivedAt = DateTimeOffset.UtcNow
        });
    }

    public async Task<List<RfidCardResponse>> ListCardsAsync(CancellationToken cancellationToken = default)
    {
        var records = await _cardRepository.ListAsync(cancellationToken);
        return records.Select(ToResponse).ToList();
    }

    public async Task<List<RfidEventResponse>> GetEventHistoryAsync(int limit, CancellationToken cancellationToken = default)
    {
        var records = await _eventRepository.GetHistoryAsync(limit, cancellationToken);

        return records.Select(r => new RfidEventResponse
        {
            Id = r.Id,
            DeviceId = r.DeviceId,
            Uid = r.Uid,
            Recognized = r.Recognized,
            ScannedAt = r.ScannedAt,
            ReceivedAt = r.ReceivedAt
        }).ToList();
    }

    public async Task<bool> AddCardAsync(string uid, string? label, CancellationToken cancellationToken = default)
    {
        var rowsAffected = await _cardRepository.InsertAsync(NormalizeUid(uid), label, cancellationToken);
        if (rowsAffected == 0)
        {
            return false;
        }

        await RepublishWhitelistAsync(cancellationToken);
        return true;
    }

    public async Task<bool> UpdateCardAsync(
        string uid, bool? isActive, string? label, CancellationToken cancellationToken = default)
    {
        var rowsAffected = await _cardRepository.UpdateAsync(NormalizeUid(uid), isActive, label, cancellationToken);
        if (rowsAffected == 0)
        {
            return false;
        }

        await RepublishWhitelistAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteCardAsync(string uid, CancellationToken cancellationToken = default)
    {
        var rowsAffected = await _cardRepository.DeleteAsync(NormalizeUid(uid), cancellationToken);
        if (rowsAffected == 0)
        {
            return false;
        }

        await RepublishWhitelistAsync(cancellationToken);
        return true;
    }

    private async Task RepublishWhitelistAsync(CancellationToken cancellationToken)
    {
        var activeUids = await _cardRepository.ListActiveUidsAsync(cancellationToken);
        var payloadJson = JsonSerializer.Serialize(activeUids);

        try
        {
            await _mqtt.PublishAsync(_mqttOptions.RfidCardsTopic, payloadJson, retain: true, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gagal publish whitelist RFID ke topic {Topic}.", _mqttOptions.RfidCardsTopic);
        }
    }

    private static string NormalizeUid(string uid) => uid.Trim().ToUpperInvariant();

    private static RfidCardResponse ToResponse(RfidCardRecord record) => new()
    {
        Uid = record.Uid,
        Label = record.Label,
        IsActive = record.IsActive,
        CreatedAt = record.CreatedAt,
        UpdatedAt = record.UpdatedAt
    };
}

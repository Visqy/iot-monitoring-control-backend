using IotBackend.Contracts;
using IotBackend.Infrastructure;
using IotBackend.Repositories;

namespace IotBackend.Services;

public sealed class DeviceService
{
    private readonly DeviceRepository _deviceRepository;
    private readonly DeviceStateRepository _deviceStateRepository;
    private readonly RealtimeBroadcaster _broadcaster;
    private readonly ILogger<DeviceService> _logger;

    public DeviceService(
        DeviceRepository deviceRepository,
        DeviceStateRepository deviceStateRepository,
        RealtimeBroadcaster broadcaster,
        ILogger<DeviceService> logger)
    {
        _deviceRepository = deviceRepository;
        _deviceStateRepository = deviceStateRepository;
        _broadcaster = broadcaster;
        _logger = logger;
    }

    public async Task<List<DeviceSummaryResponse>> ListDevicesAsync(CancellationToken cancellationToken = default)
    {
        var devices = await _deviceRepository.ListAsync(cancellationToken);

        return devices.Select(d => new DeviceSummaryResponse
        {
            DeviceId = d.DeviceId,
            Name = d.Name,
            Location = d.Location,
            Status = d.Status,
            UpdatedAt = d.UpdatedAt
        }).ToList();
    }

    public async Task<DeviceStateResponse?> GetDeviceStateAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        var state = await _deviceStateRepository.GetByDeviceIdAsync(deviceId, cancellationToken);
        if (state is null)
        {
            return null;
        }

        return new DeviceStateResponse
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
        };
    }

    public async Task ProcessStatusMessageAsync(string deviceId, string rawPayload, CancellationToken cancellationToken = default)
    {
        var status = rawPayload.Trim().ToLowerInvariant();
        if (status is not ("online" or "offline"))
        {
            _logger.LogWarning("Payload status dari {DeviceId} tidak dikenali, diabaikan. Raw: {Raw}", deviceId, rawPayload);
            return;
        }

        var lastSeen = status == "online" ? DateTimeOffset.UtcNow : (DateTimeOffset?)null;
        var rowsAffected = await _deviceStateRepository.UpdateStatusAsync(deviceId, status, lastSeen, cancellationToken);
        if (rowsAffected == 0)
        {
            _logger.LogWarning(
                "device_current_state untuk {DeviceId} belum ada (belum pernah kirim telemetry), status '{Status}' tidak tersimpan.",
                deviceId, status);
            return;
        }

        var state = await GetDeviceStateAsync(deviceId, cancellationToken);
        if (state is not null)
        {
            _broadcaster.Publish("device-state", state);
        }
    }
}

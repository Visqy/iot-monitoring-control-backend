namespace IotBackend.Models;

public sealed class DeviceCurrentStateRecord
{
    public required string DeviceId { get; init; }
    public required string Status { get; init; }

    public double? VoltageB { get; init; }
    public double? CurrentB { get; init; }
    public double? PowerB { get; init; }
    public double? EnergyB { get; init; }
    public double? FrequencyB { get; init; }
    public bool? RelayState { get; init; }

    public DateTimeOffset? LastSeen { get; init; }
}

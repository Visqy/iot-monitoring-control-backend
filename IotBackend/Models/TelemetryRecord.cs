namespace IotBackend.Models;

public sealed class TelemetryRecord
{
    public required string DeviceId { get; init; }
    public required string Topic { get; init; }

    public double? VoltageB { get; init; }
    public double? CurrentB { get; init; }
    public double? PowerB { get; init; }
    public double? EnergyB { get; init; }
    public double? FrequencyB { get; init; }

    public DateTime? DeviceTimestamp { get; init; }

    public required string RawPayload { get; init; }
}

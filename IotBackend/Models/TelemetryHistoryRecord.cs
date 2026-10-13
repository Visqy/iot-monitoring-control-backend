namespace IotBackend.Models;

public sealed class TelemetryHistoryRecord
{
    public long Id { get; init; }
    public required string DeviceId { get; init; }
    public required string Topic { get; init; }

    public double? VoltageB { get; init; }
    public double? CurrentB { get; init; }
    public double? PowerB { get; init; }
    public double? EnergyB { get; init; }
    public double? FrequencyB { get; init; }

    public DateTimeOffset? DeviceTimestamp { get; init; }
    public DateTimeOffset ReceivedAt { get; init; }
}

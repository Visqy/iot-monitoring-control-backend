namespace IotBackend.Models;

public sealed class TelemetryPayload
{
    public double? VoltageB { get; init; }
    public double? CurrentB { get; init; }
    public double? PowerB { get; init; }
    public double? EnergyB { get; init; }
    public double? FreqB { get; init; }

    public string? Timestamp { get; init; }
}

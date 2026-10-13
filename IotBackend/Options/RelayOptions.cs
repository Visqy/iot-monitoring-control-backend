namespace IotBackend.Options;

public sealed class RelayOptions
{
    public const string SectionName = "Relay";

    public int CommandTimeoutSeconds { get; init; } = 30;

    public int TimeoutScanIntervalSeconds { get; init; } = 10;
}

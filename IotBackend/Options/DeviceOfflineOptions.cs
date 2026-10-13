namespace IotBackend.Options;

public sealed class DeviceOfflineOptions
{
    public const string SectionName = "DeviceOffline";

    public int OfflineThresholdSeconds { get; init; } = 165;

    public int SweepIntervalSeconds { get; init; } = 30;
}

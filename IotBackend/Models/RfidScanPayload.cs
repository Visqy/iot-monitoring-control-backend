namespace IotBackend.Models;

public sealed class RfidScanPayload
{
    public string? Uid { get; init; }
    public bool Recognized { get; init; }
}

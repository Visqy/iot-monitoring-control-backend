namespace IotBackend.Contracts;

public sealed class RfidEventResponse
{
    public long Id { get; init; }
    public required string DeviceId { get; init; }
    public required string Uid { get; init; }
    public bool Recognized { get; init; }
    public DateTimeOffset? ScannedAt { get; init; }
    public DateTimeOffset ReceivedAt { get; init; }
}

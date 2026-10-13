namespace IotBackend.Models;

public sealed class RelayCommandPayload
{
    public Guid CommandId { get; init; }
    public required string State { get; init; }
    public required string Source { get; init; }
    public DateTimeOffset IssuedAt { get; init; }
}

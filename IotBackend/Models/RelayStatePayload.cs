namespace IotBackend.Models;

public sealed class RelayStatePayload
{
    public Guid? CommandId { get; init; }
    public string? State { get; init; }
    public string? ExecutedAt { get; init; }

    public string? Source { get; init; }
}

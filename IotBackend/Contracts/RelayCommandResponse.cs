namespace IotBackend.Contracts;

public sealed class RelayCommandResponse
{
    public Guid CommandId { get; init; }
    public required string DeviceId { get; init; }
    public bool RequestedState { get; init; }
    public required string Status { get; init; }
}

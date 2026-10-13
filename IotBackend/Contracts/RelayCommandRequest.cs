namespace IotBackend.Contracts;

public sealed class RelayCommandRequest
{
    public required bool State { get; init; }
}

namespace IotBackend.Contracts;

public sealed class CreateRfidCardRequest
{
    public required string Uid { get; init; }
    public string? Label { get; init; }
}

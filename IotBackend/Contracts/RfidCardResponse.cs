namespace IotBackend.Contracts;

public sealed class RfidCardResponse
{
    public required string Uid { get; init; }
    public string? Label { get; init; }
    public bool IsActive { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

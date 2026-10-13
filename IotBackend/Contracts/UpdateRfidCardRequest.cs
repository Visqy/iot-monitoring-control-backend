namespace IotBackend.Contracts;

public sealed class UpdateRfidCardRequest
{
    public bool? IsActive { get; init; }
    public string? Label { get; init; }
}

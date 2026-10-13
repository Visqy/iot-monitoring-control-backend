namespace IotBackend.Models;

public sealed class DeviceRecord
{
    public required string DeviceId { get; init; }
    public string? Name { get; init; }
    public string? Location { get; init; }
    public required string Status { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

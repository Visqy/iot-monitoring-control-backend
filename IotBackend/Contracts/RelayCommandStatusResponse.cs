namespace IotBackend.Contracts;

public sealed class RelayCommandStatusResponse
{
    public Guid CommandId { get; init; }
    public required string DeviceId { get; init; }

    public bool RequestedState { get; init; }
    public bool? ActualState { get; init; }

    public required string Status { get; init; }

    public DateTimeOffset RequestedAt { get; init; }
    public DateTimeOffset? SentAt { get; init; }
    public DateTimeOffset? AcknowledgedAt { get; init; }

    public string? ErrorMessage { get; init; }
}

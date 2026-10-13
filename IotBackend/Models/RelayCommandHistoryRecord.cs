namespace IotBackend.Models;

public sealed class RelayCommandHistoryRecord
{
    public required string CommandId { get; init; }
    public required string DeviceId { get; init; }

    public bool RequestedState { get; init; }
    public bool? ActualState { get; init; }

    public required string Source { get; init; }
    public required string Status { get; init; }

    public DateTimeOffset RequestedAt { get; init; }
    public DateTimeOffset? SentAt { get; init; }
    public DateTimeOffset? AcknowledgedAt { get; init; }

    public string? ErrorMessage { get; init; }
}

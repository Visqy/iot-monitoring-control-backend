namespace IotBackend.Infrastructure;

public interface IMqttPublisher
{
    Task PublishAsync(string topic, string payload, bool retain = false, CancellationToken cancellationToken = default);
}

using IotBackend.Infrastructure;

namespace IotBackend.Tests.Support;

public sealed class FakeMqttPublisher : IMqttPublisher
{
    private readonly List<PublishedMessage> _published = new();

    public IReadOnlyList<PublishedMessage> Published => _published;

    public Func<PublishedMessage, Task>? OnPublish { get; set; }

    public Exception? ExceptionToThrow { get; set; }

    public async Task PublishAsync(string topic, string payload, bool retain = false, CancellationToken cancellationToken = default)
    {
        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        var message = new PublishedMessage(topic, payload, retain);
        _published.Add(message);

        if (OnPublish is not null)
        {
            await OnPublish(message);
        }
    }
}

public sealed record PublishedMessage(string Topic, string Payload, bool Retain);

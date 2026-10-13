using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;

namespace IotBackend.Infrastructure;

public sealed class RealtimeBroadcaster
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ConcurrentDictionary<Guid, ChannelWriter<string>> _clients = new();

    public Guid Subscribe(ChannelWriter<string> writer)
    {
        var clientId = Guid.NewGuid();
        _clients[clientId] = writer;
        return clientId;
    }

    public void Unsubscribe(Guid clientId)
    {
        _clients.TryRemove(clientId, out _);
    }

    public void Publish(string eventName, object payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var frame = $"event: {eventName}\ndata: {json}\n\n";

        foreach (var writer in _clients.Values)
        {
            writer.TryWrite(frame);
        }
    }
}

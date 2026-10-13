using IotBackend.Options;
using Microsoft.Extensions.Options;
using MQTTnet;

namespace IotBackend.Infrastructure;

public sealed class MqttClientService : IMqttPublisher, IAsyncDisposable
{
    private readonly MqttOptions _options;
    private readonly ILogger<MqttClientService> _logger;
    private readonly IMqttClient _client;
    private readonly MqttClientOptions _clientOptions;

    public MqttClientService(IOptions<MqttOptions> options, ILogger<MqttClientService> logger)
    {
        _options = options.Value;
        _logger = logger;

        var factory = new MqttClientFactory();
        _client = factory.CreateMqttClient();

        var optionsBuilder = new MqttClientOptionsBuilder()
            .WithClientId(_options.ClientId)
            .WithTcpServer(_options.Host, _options.Port)
            .WithCredentials(_options.Username, _options.Password)
            .WithCleanSession();

        if (_options.UseTls)
        {
            optionsBuilder.WithTlsOptions(o => o.UseTls());
        }

        _clientOptions = optionsBuilder.Build();
    }

    public bool IsConnected => _client.IsConnected;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_client.IsConnected)
        {
            return;
        }

        _logger.LogInformation(
            "Menyambung ke MQTT broker {Host}:{Port} (TLS: {UseTls}) sebagai clientId '{ClientId}'...",
            _options.Host, _options.Port, _options.UseTls, _options.ClientId);

        var result = await _client.ConnectAsync(_clientOptions, cancellationToken);

        _logger.LogInformation("Hasil connect MQTT: {ResultCode}", result.ResultCode);
    }

    public void OnApplicationMessage(Func<MqttApplicationMessageReceivedEventArgs, Task> handler)
    {
        _client.ApplicationMessageReceivedAsync += handler;
    }

    public async Task SubscribeAsync(string topicFilter, CancellationToken cancellationToken = default)
    {
        var subscribeOptions = new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(f => f.WithTopic(topicFilter))
            .Build();

        await _client.SubscribeAsync(subscribeOptions, cancellationToken);
        _logger.LogInformation("Subscribed ke topic MQTT {Topic}", topicFilter);
    }

    public async Task PublishAsync(string topic, string payload, bool retain = false, CancellationToken cancellationToken = default)
    {
        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .WithRetainFlag(retain)
            .Build();

        await _client.PublishAsync(message, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_client.IsConnected)
            {
                await _client.DisconnectAsync();
            }
        }
        finally
        {
            _client.Dispose();
        }
    }
}

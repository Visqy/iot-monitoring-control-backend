using IotBackend.Infrastructure;
using IotBackend.Options;
using IotBackend.Services;
using Microsoft.Extensions.Options;
using MQTTnet;

namespace IotBackend.BackgroundServices;

public sealed class MqttSubscriberService : BackgroundService
{
    private readonly MqttClientService _mqtt;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MqttOptions _options;
    private readonly ILogger<MqttSubscriberService> _logger;

    public MqttSubscriberService(
        MqttClientService mqtt,
        IServiceScopeFactory scopeFactory,
        IOptions<MqttOptions> options,
        ILogger<MqttSubscriberService> logger)
    {
        _mqtt = mqtt;
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _mqtt.OnApplicationMessage(HandleMessageAsync);

        var reconnectDelay = TimeSpan.FromSeconds(Math.Max(1, _options.ReconnectDelaySeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_mqtt.IsConnected)
                {
                    await _mqtt.ConnectAsync(stoppingToken);
                    await SubscribeAllAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "MQTT belum tersambung. Coba lagi dalam {Delay} detik.", reconnectDelay.TotalSeconds);
            }

            try
            {
                await Task.Delay(reconnectDelay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task SubscribeAllAsync(CancellationToken cancellationToken)
    {
        await _mqtt.SubscribeAsync(_options.TelemetryTopic, cancellationToken);
        await _mqtt.SubscribeAsync(_options.RelayStateTopic, cancellationToken);
        await _mqtt.SubscribeAsync(_options.StatusTopic, cancellationToken);
        await _mqtt.SubscribeAsync(_options.RfidTopic, cancellationToken);
    }

    private async Task HandleMessageAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        var topic = e.ApplicationMessage.Topic;
        var deviceId = ExtractDeviceId(topic);
        var payloadText = e.ApplicationMessage.ConvertPayloadToString() ?? string.Empty;

        if (string.IsNullOrEmpty(deviceId))
        {
            _logger.LogWarning("Topic '{Topic}' tidak punya segmen device_id, pesan diabaikan.", topic);
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();

            if (topic.EndsWith("/pzem", StringComparison.Ordinal))
            {
                var telemetryService = scope.ServiceProvider.GetRequiredService<TelemetryService>();
                await telemetryService.ProcessTelemetryAsync(deviceId, topic, payloadText);
            }
            else if (topic.EndsWith("/relay/state", StringComparison.Ordinal))
            {
                var relayCommandService = scope.ServiceProvider.GetRequiredService<RelayCommandService>();
                await relayCommandService.ProcessRelayStateAsync(deviceId, payloadText);
            }
            else if (topic.EndsWith("/status", StringComparison.Ordinal))
            {
                var deviceService = scope.ServiceProvider.GetRequiredService<DeviceService>();
                await deviceService.ProcessStatusMessageAsync(deviceId, payloadText);
            }
            else if (topic.EndsWith("/rfid", StringComparison.Ordinal))
            {
                var rfidService = scope.ServiceProvider.GetRequiredService<RfidService>();
                await rfidService.ProcessScanAsync(deviceId, payloadText);
            }
            else
            {
                _logger.LogWarning("Topic '{Topic}' tidak dikenali, pesan diabaikan.", topic);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gagal memproses pesan MQTT dari topic {Topic}.", topic);
        }
    }

    internal static string? ExtractDeviceId(string topic)
    {
        if (string.IsNullOrEmpty(topic))
        {
            return null;
        }

        var slashIndex = topic.IndexOf('/');
        return slashIndex <= 0 ? null : topic[..slashIndex];
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using IotBackend.Infrastructure;
using IotBackend.Tests.Support;
using MQTTnet;
using MQTTnet.Server;
using Xunit;

namespace IotBackend.Tests.Api;

/// <summary>
/// Hardware-free end-to-end check: the real application (MQTT subscriber, services, repositories,
/// PostgreSQL) talks to an in-process MQTT broker, and a scripted MQTT client plays the ESP32.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class MqttEndToEndTests : IAsyncLifetime
{
    private MqttServer? _broker;
    private IMqttClient? _device;
    private ApiFactory? _factory;
    private readonly List<(string Topic, string Payload)> _receivedByDevice = new();
    private readonly List<(string ClientId, string Topic, MQTTnet.Protocol.MqttQualityOfServiceLevel Qos, bool Retain)> _seenByBroker = new();

    public async Task InitializeAsync()
    {
        if (!TestDatabase.IsConfigured)
        {
            return;
        }

        await TestDatabase.ResetAsync();

        var port = GetFreePort();
        _broker = new MqttServerFactory().CreateMqttServer(
            new MqttServerOptionsBuilder().WithDefaultEndpoint().WithDefaultEndpointPort(port).Build());
        _broker.InterceptingPublishAsync += e =>
        {
            lock (_seenByBroker)
            {
                _seenByBroker.Add((e.ClientId, e.ApplicationMessage.Topic, e.ApplicationMessage.QualityOfServiceLevel, e.ApplicationMessage.Retain));
            }

            return Task.CompletedTask;
        };
        await _broker.StartAsync();

        _device = new MqttClientFactory().CreateMqttClient();
        _device.ApplicationMessageReceivedAsync += e =>
        {
            lock (_receivedByDevice)
            {
                _receivedByDevice.Add((e.ApplicationMessage.Topic, e.ApplicationMessage.ConvertPayloadToString() ?? string.Empty));
            }

            return Task.CompletedTask;
        };
        await _device.ConnectAsync(new MqttClientOptionsBuilder().WithTcpServer("127.0.0.1", port).WithClientId("simulated-esp32").Build());
        await _device.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(f => f.WithTopic("terminal1/relay/set"))
            .WithTopicFilter(f => f.WithTopic("rfid/cards"))
            .Build());

        _factory = new ApiFactory(TestDatabase.ConnectionString!, useRealMqtt: true, new Dictionary<string, string>
        {
            ["Mqtt__Host"] = "127.0.0.1",
            ["Mqtt__Port"] = port.ToString(),
            ["Mqtt__UseTls"] = "false",
            ["Mqtt__ClientId"] = "iot-backend-e2e",
            ["Mqtt__ReconnectDelaySeconds"] = "1"
        });
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_device is { IsConnected: true })
        {
            await _device.DisconnectAsync();
        }

        _device?.Dispose();
        if (_broker is not null)
        {
            await _broker.StopAsync();
            _broker.Dispose();
        }
    }

    [RequiresPostgresFact]
    public async Task SimulatedDevice_TelemetryStatusAndRelayConfirmation_FlowThroughTheRealPipeline()
    {
        var client = await _factory!.CreateAuthenticatedClientAsync();
        await WaitUntilAsync(() => _factory.Services.GetService(typeof(MqttClientService)) is MqttClientService { IsConnected: true },
            "backend to connect to the broker");

        var telemetryStored = await PublishUntilAsync(
            "terminal1/pzem",
            "{\"voltageB\":220.1,\"currentB\":0.5,\"powerB\":100,\"energyB\":3.5,\"freqB\":50,\"timestamp\":\"2026-07-23T13:51:10+07:00\"}",
            async () => (await client.GetAsync("/api/devices/terminal1/state")).StatusCode == HttpStatusCode.OK);
        Assert.True(telemetryStored, "telemetry published by the simulated device should reach the database");

        await _device!.PublishStringAsync("terminal1/status", "offline");
        await WaitUntilAsync(() => StatusOf(client).GetAwaiter().GetResult() == "offline", "offline status to be applied");

        var post = await client.PostAsJsonAsync("/api/devices/terminal1/relay", new { state = true });
        Assert.Equal(HttpStatusCode.Accepted, post.StatusCode);
        var accepted = await post.Content.ReadFromJsonAsync<JsonElement>();
        var commandId = accepted.GetProperty("commandId").GetGuid();

        await WaitUntilAsync(() => ReceivedByDevice().Any(m => m.Topic == "terminal1/relay/set"), "relay/set to reach the device");
        var relaySet = ReceivedByDevice().First(m => m.Topic == "terminal1/relay/set");
        Assert.Contains(commandId.ToString(), relaySet.Payload);

        var stillSent = await client.GetFromJsonAsync<JsonElement>($"/api/commands/{commandId}");
        Assert.Equal("sent", stillSent.GetProperty("status").GetString());

        await _device.PublishStringAsync(
            "terminal1/relay/state",
            $"{{\"commandId\":\"{commandId}\",\"state\":\"ON\",\"source\":\"dashboard\",\"executedAt\":\"2026-07-23T13:52:00+07:00\"}}");

        await WaitUntilAsync(
            () => CommandStatus(client, commandId).GetAwaiter().GetResult() == "executed",
            "command to be confirmed by relay/state");
        var state = await client.GetFromJsonAsync<JsonElement>("/api/devices/terminal1/state");
        Assert.True(state.GetProperty("relayState").GetBoolean());

        await _device.PublishStringAsync("terminal1/rfid", "{\"uid\":\"04a1b2c3\",\"recognized\":false}");
        await WaitUntilAsync(
            () => client.GetFromJsonAsync<JsonElement>("/api/rfid-events").GetAwaiter().GetResult().GetArrayLength() == 1,
            "rfid scan to be logged");

        await client.PostAsJsonAsync("/api/rfid-cards", new { uid = "04A1B2C3" });
        await WaitUntilAsync(() => ReceivedByDevice().Any(m => m.Topic == "rfid/cards" && m.Payload == "[\"04A1B2C3\"]"),
            "retained whitelist to reach the device");

        List<(string ClientId, string Topic, MQTTnet.Protocol.MqttQualityOfServiceLevel Qos, bool Retain)> seen;
        lock (_seenByBroker)
        {
            seen = _seenByBroker.Where(m => m.ClientId == "iot-backend-e2e").ToList();
        }

        var relaySetPublish = Assert.Single(seen, m => m.Topic == "terminal1/relay/set");
        Assert.Equal(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtMostOnce, relaySetPublish.Qos);
        Assert.False(relaySetPublish.Retain);

        var whitelistPublish = Assert.Single(seen, m => m.Topic == "rfid/cards");
        Assert.Equal(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtMostOnce, whitelistPublish.Qos);
        Assert.True(whitelistPublish.Retain);
    }

    private IReadOnlyList<(string Topic, string Payload)> ReceivedByDevice()
    {
        lock (_receivedByDevice)
        {
            return _receivedByDevice.ToList();
        }
    }

    private static async Task<string?> StatusOf(HttpClient client)
    {
        var response = await client.GetAsync("/api/devices/terminal1/state");
        return response.IsSuccessStatusCode
            ? (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString()
            : null;
    }

    private static async Task<string?> CommandStatus(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<JsonElement>($"/api/commands/{id}")).GetProperty("status").GetString();

    private async Task<bool> PublishUntilAsync(string topic, string payload, Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            await _device!.PublishStringAsync(topic, payload);
            await Task.Delay(300);
            if (await condition())
            {
                return true;
            }
        }

        return false;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string description)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(150);
        }

        throw new TimeoutException($"Timed out waiting for {description}.");
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

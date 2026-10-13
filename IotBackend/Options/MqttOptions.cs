namespace IotBackend.Options;

public sealed class MqttOptions
{
    public const string SectionName = "Mqtt";

    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 8883;
    public string Username { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string ClientId { get; init; } = "iot-backend-poc";

    public bool UseTls { get; init; } = true;

    public string TelemetryTopic { get; init; } = "+/pzem";
    public string StatusTopic { get; init; } = "+/status";
    public string RelayStateTopic { get; init; } = "+/relay/state";
    public string RfidTopic { get; init; } = "+/rfid";
    public string RfidCardsTopic { get; init; } = "rfid/cards";

    public int ReconnectDelaySeconds { get; init; } = 5;
}

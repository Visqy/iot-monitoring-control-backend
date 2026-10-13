using System.Text.Json;
using IotBackend.Models;
using IotBackend.Services;
using Xunit;

namespace IotBackend.Tests.Unit;

public class PayloadParsingTests
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public void RelayState_WithGuidCommandId_IsParsed()
    {
        var id = Guid.NewGuid();
        var json = "{\"commandId\":\"" + id + "\",\"state\":\"ON\",\"source\":\"dashboard\",\"executedAt\":\"2026-07-15T16:15:55+07:00\"}";

        var payload = JsonSerializer.Deserialize<RelayStatePayload>(json, Options)!;

        Assert.Equal(id, payload.CommandId);
        Assert.Equal("ON", payload.State);
        Assert.Equal("dashboard", payload.Source);
        Assert.Equal("2026-07-15T16:15:55+07:00", payload.ExecutedAt);
    }

    [Fact]
    public void RelayState_WithNullCommandId_IsDeviceInitiated()
    {
        var payload = JsonSerializer.Deserialize<RelayStatePayload>(
            """{"commandId":null,"state":"OFF","source":"rfid"}""", Options)!;

        Assert.Null(payload.CommandId);
        Assert.Equal("rfid", payload.Source);
    }

    [Fact]
    public void RelayState_WithNonGuidCommandId_IsRejectedByJsonParser()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<RelayStatePayload>("""{"commandId":"rfid","state":"ON"}""", Options));
    }

    [Theory]
    [InlineData("ON", true)]
    [InlineData("on", true)]
    [InlineData("  Off ", false)]
    [InlineData("OFF", false)]
    [InlineData("toggle", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ParseMqttState_MapsOnOffAndRejectsEverythingElse(string? input, bool? expected)
    {
        Assert.Equal(expected, RelayCommandService.ParseMqttState(input));
    }

    [Fact]
    public void ToMqttState_UsesUppercaseOnOff()
    {
        Assert.Equal("ON", RelayCommandService.ToMqttState(true));
        Assert.Equal("OFF", RelayCommandService.ToMqttState(false));
    }

    [Fact]
    public void Telemetry_AllowsNullMeasurements()
    {
        const string json =
            """{"voltageB":211.7,"currentB":null,"powerB":null,"energyB":0,"freqB":49.89,"timestamp":"2026-07-23 13:51:10"}""";

        var payload = JsonSerializer.Deserialize<TelemetryPayload>(json, Options)!;

        Assert.Equal(211.7, payload.VoltageB);
        Assert.Null(payload.CurrentB);
        Assert.Null(payload.PowerB);
        Assert.Equal(0, payload.EnergyB);
        Assert.Equal(49.89, payload.FreqB);
    }

    [Fact]
    public void Telemetry_IgnoresUnknownLegacyFields()
    {
        var payload = JsonSerializer.Deserialize<TelemetryPayload>(
            """{"voltageA":207.7,"voltageB":207.6,"freqB":49.9}""", Options)!;

        Assert.Equal(207.6, payload.VoltageB);
    }

    [Fact]
    public void Telemetry_WithMalformedJson_Throws()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TelemetryPayload>("not json", Options));
    }

    [Fact]
    public void RfidScan_ParsesUidAndRecognizedFlag()
    {
        var payload = JsonSerializer.Deserialize<RfidScanPayload>("""{"uid":"04a1b2c3","recognized":true}""", Options)!;

        Assert.Equal("04a1b2c3", payload.Uid);
        Assert.True(payload.Recognized);
    }
}

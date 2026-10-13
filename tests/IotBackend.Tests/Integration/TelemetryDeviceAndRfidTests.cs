using System.Text.Json;
using IotBackend.Infrastructure;
using IotBackend.Options;
using IotBackend.Repositories;
using IotBackend.Services;
using IotBackend.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IotBackend.Tests.Integration;

[Collection(DatabaseCollection.Name)]
public class TelemetryDeviceAndRfidTests : IAsyncLifetime
{
    private readonly FakeMqttPublisher _publisher = new();
    private readonly RealtimeBroadcaster _broadcaster = new();
    private TelemetryService _telemetry = null!;
    private DeviceService _devices = null!;
    private RfidService _rfid = null!;
    private DeviceStateRepository _state = null!;
    private TelemetryRepository _telemetryRepository = null!;
    private RfidCardRepository _cards = null!;
    private RfidEventRepository _events = null!;

    public async Task InitializeAsync()
    {
        if (!TestDatabase.IsConfigured)
        {
            return;
        }

        await TestDatabase.ResetAsync();
        var ds = TestDatabase.DataSource;
        _state = new DeviceStateRepository(ds);
        _telemetryRepository = new TelemetryRepository(ds);
        var deviceRepository = new DeviceRepository(ds);
        _cards = new RfidCardRepository(ds);
        _events = new RfidEventRepository(ds);

        _telemetry = new TelemetryService(
            _telemetryRepository, _state, deviceRepository, _broadcaster, NullLogger<TelemetryService>.Instance);
        _devices = new DeviceService(deviceRepository, _state, _broadcaster, NullLogger<DeviceService>.Instance);
        _rfid = new RfidService(
            _cards, _events, _publisher,
            Microsoft.Extensions.Options.Options.Create(new MqttOptions()),
            _broadcaster, NullLogger<RfidService>.Instance);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [RequiresPostgresFact]
    public async Task Telemetry_IsStored_StateUpserted_AndDeviceAutoRegistered()
    {
        await _telemetry.ProcessTelemetryAsync(
            "terminal1", "terminal1/pzem",
            "{\"voltageB\":220.5,\"currentB\":1.5,\"powerB\":330,\"energyB\":12.5,\"freqB\":50.0,\"timestamp\":\"2026-07-23 13:51:10\"}");

        var history = await _telemetry.GetHistoryAsync("terminal1", null, null, 10);
        var row = Assert.Single(history);
        Assert.Equal(220.5, row.VoltageB);
        Assert.Equal(12.5, row.EnergyB);
        Assert.Equal(new DateTimeOffset(2026, 7, 23, 6, 51, 10, TimeSpan.Zero), row.DeviceTimestamp);

        var state = await _devices.GetDeviceStateAsync("terminal1");
        Assert.NotNull(state);
        Assert.Equal("online", state!.Status);
        Assert.Equal(330, state.PowerB);

        var listed = Assert.Single(await _devices.ListDevicesAsync());
        Assert.Equal("terminal1", listed.DeviceId);
        Assert.Equal("online", listed.Status);
    }

    [RequiresPostgresFact]
    public async Task Telemetry_WithNullMeasurements_IsStillStored()
    {
        await _telemetry.ProcessTelemetryAsync(
            "terminal1", "terminal1/pzem",
            "{\"voltageB\":211.7,\"currentB\":null,\"powerB\":null,\"energyB\":0,\"freqB\":49.89}");

        var row = Assert.Single(await _telemetry.GetHistoryAsync("terminal1", null, null, 10));
        Assert.Null(row.CurrentB);
        Assert.Null(row.PowerB);
    }

    [RequiresPostgresFact]
    public async Task Telemetry_WithInvalidJson_IsIgnored()
    {
        await _telemetry.ProcessTelemetryAsync("terminal1", "terminal1/pzem", "not json");

        Assert.Empty(await _telemetry.GetHistoryAsync("terminal1", null, null, 10));
        Assert.Null(await _devices.GetDeviceStateAsync("terminal1"));
    }

    [RequiresPostgresFact]
    public async Task Telemetry_Publishes_DeviceStateEvent_ToSseSubscribers()
    {
        var channel = System.Threading.Channels.Channel.CreateUnbounded<string>();
        _broadcaster.Subscribe(channel.Writer);

        await _telemetry.ProcessTelemetryAsync("terminal1", "terminal1/pzem", "{\"voltageB\":220}");

        Assert.True(channel.Reader.TryRead(out var frame));
        Assert.StartsWith("event: device-state\ndata: ", frame);
        Assert.Contains("\"deviceId\":\"terminal1\"", frame);
    }

    [RequiresPostgresFact]
    public async Task StatusMessages_UpdateAvailability_AndIgnoreGarbage()
    {
        await _telemetry.ProcessTelemetryAsync("terminal1", "terminal1/pzem", "{\"voltageB\":220}");

        await _devices.ProcessStatusMessageAsync("terminal1", "offline");
        Assert.Equal("offline", (await _devices.GetDeviceStateAsync("terminal1"))!.Status);

        await _devices.ProcessStatusMessageAsync("terminal1", " ONLINE ");
        Assert.Equal("online", (await _devices.GetDeviceStateAsync("terminal1"))!.Status);

        await _devices.ProcessStatusMessageAsync("terminal1", "banana");
        Assert.Equal("online", (await _devices.GetDeviceStateAsync("terminal1"))!.Status);
    }

    [RequiresPostgresFact]
    public async Task OfflineSweep_MarksOnlyDevicesNotSeenSinceCutoff()
    {
        await _telemetry.ProcessTelemetryAsync("terminal1", "terminal1/pzem", "{\"voltageB\":220}");

        Assert.Equal(0, await _state.MarkOfflineStaleAsync(DateTimeOffset.UtcNow.AddMinutes(-5)));
        Assert.Equal("online", (await _devices.GetDeviceStateAsync("terminal1"))!.Status);

        Assert.Equal(1, await _state.MarkOfflineStaleAsync(DateTimeOffset.UtcNow.AddMinutes(5)));
        Assert.Equal("offline", (await _devices.GetDeviceStateAsync("terminal1"))!.Status);
    }

    [RequiresPostgresFact]
    public async Task RfidScan_IsLogged_WithNormalisedUid_IncludingRejectedCards()
    {
        await _rfid.ProcessScanAsync("terminal1", "{\"uid\":\" 04a1b2c3 \",\"recognized\":false}");
        await _rfid.ProcessScanAsync("terminal1", "{\"uid\":\"04D5E6F7\",\"recognized\":true}");
        await _rfid.ProcessScanAsync("terminal1", "{\"recognized\":true}");
        await _rfid.ProcessScanAsync("terminal1", "garbage");

        var history = await _rfid.GetEventHistoryAsync(10);
        Assert.Equal(2, history.Count);
        Assert.Contains(history, e => e.Uid == "04A1B2C3" && !e.Recognized);
        Assert.Contains(history, e => e.Uid == "04D5E6F7" && e.Recognized);
    }

    [RequiresPostgresFact]
    public async Task Whitelist_Changes_RepublishRetainedListOfActiveUidsOnly()
    {
        Assert.True(await _rfid.AddCardAsync("aa11", "first"));
        Assert.True(await _rfid.AddCardAsync("bb22", null));
        Assert.False(await _rfid.AddCardAsync("AA11", null));
        Assert.True(await _rfid.UpdateCardAsync("bb22", isActive: false, label: null));

        var last = _publisher.Published[^1];
        Assert.Equal("rfid/cards", last.Topic);
        Assert.True(last.Retain);
        Assert.Equal(new[] { "AA11" }, JsonSerializer.Deserialize<string[]>(last.Payload));

        Assert.True(await _rfid.DeleteCardAsync("aa11"));
        Assert.Empty(JsonSerializer.Deserialize<string[]>(_publisher.Published[^1].Payload)!);
        Assert.False(await _rfid.DeleteCardAsync("zz99"));
    }

    [RequiresPostgresFact]
    public async Task WhitelistRepublishFailure_DoesNotFailTheDatabaseOperation()
    {
        _publisher.ExceptionToThrow = new InvalidOperationException("broker down");

        Assert.True(await _rfid.AddCardAsync("cc33", "kept in db"));

        Assert.Single(await _rfid.ListCardsAsync());
    }
}

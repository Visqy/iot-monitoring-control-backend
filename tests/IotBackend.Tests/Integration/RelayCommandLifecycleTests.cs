using System.Text.Json;
using IotBackend.Infrastructure;
using IotBackend.Models;
using IotBackend.Repositories;
using IotBackend.Services;
using IotBackend.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IotBackend.Tests.Integration;

[Collection(DatabaseCollection.Name)]
public class RelayCommandLifecycleTests : IAsyncLifetime
{
    private const string DeviceId = "terminal1";

    private readonly FakeMqttPublisher _publisher = new();
    private RelayCommandRepository _commands = null!;
    private DeviceStateRepository _state = null!;
    private RelayCommandService _service = null!;

    public async Task InitializeAsync()
    {
        if (!TestDatabase.IsConfigured)
        {
            return;
        }

        await TestDatabase.ResetAsync();
        _commands = new RelayCommandRepository(TestDatabase.DataSource);
        _state = new DeviceStateRepository(TestDatabase.DataSource);
        _service = new RelayCommandService(
            _commands, _state, _publisher, new RealtimeBroadcaster(), NullLogger<RelayCommandService>.Instance);

        await _state.UpsertFromTelemetryAsync(new DeviceCurrentState
        {
            DeviceId = DeviceId,
            Status = "online",
            LastSeen = DateTimeOffset.UtcNow
        });
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static string StatePayload(Guid commandId, string state) =>
        "{\"commandId\":\"" + commandId + "\",\"state\":\"" + state + "\",\"source\":\"dashboard\"}";

    private async Task<RelayCommandRecord> GetAsync(Guid id) =>
        (await _commands.GetByIdAsync(id)) ?? throw new InvalidOperationException("command row not found");

    [RequiresPostgresFact]
    public async Task NormalFlow_PendingToSentToExecuted()
    {
        var response = await _service.SendCommandAsync(DeviceId, requestedState: true);

        Assert.Equal("sent", response.Status);
        var sent = await GetAsync(response.CommandId);
        Assert.Equal("sent", sent.Status);
        Assert.NotNull(sent.SentAt);
        Assert.Null(sent.ActualState);
        Assert.Null(sent.AcknowledgedAt);

        var message = Assert.Single(_publisher.Published);
        Assert.Equal("terminal1/relay/set", message.Topic);
        Assert.False(message.Retain);
        using var published = JsonDocument.Parse(message.Payload);
        Assert.Equal(response.CommandId.ToString(), published.RootElement.GetProperty("commandId").GetString());
        Assert.Equal("ON", published.RootElement.GetProperty("state").GetString());

        await _service.ProcessRelayStateAsync(DeviceId, StatePayload(response.CommandId, "ON"));

        var executed = await GetAsync(response.CommandId);
        Assert.Equal("executed", executed.Status);
        Assert.True(executed.ActualState);
        Assert.NotNull(executed.AcknowledgedAt);
        Assert.True((await _state.GetByDeviceIdAsync(DeviceId))!.RelayState);
    }

    [RequiresPostgresFact]
    public async Task PublishingIsNotConfirmation_CommandStaysSentUntilDeviceReports()
    {
        var response = await _service.SendCommandAsync(DeviceId, requestedState: true);

        var record = await GetAsync(response.CommandId);
        Assert.Equal("sent", record.Status);
        Assert.Null((await _state.GetByDeviceIdAsync(DeviceId))!.RelayState);
    }

    [RequiresPostgresFact]
    public async Task EarlyAcknowledgement_BeforeMarkSent_IsNotDowngradedToSent()
    {
        _publisher.OnPublish = async message =>
        {
            using var doc = JsonDocument.Parse(message.Payload);
            var id = doc.RootElement.GetProperty("commandId").GetGuid();
            await _service.ProcessRelayStateAsync(DeviceId, StatePayload(id, "ON"));
        };

        var response = await _service.SendCommandAsync(DeviceId, requestedState: true);

        var record = await GetAsync(response.CommandId);
        Assert.Equal("executed", record.Status);
        Assert.True(record.ActualState);
        Assert.NotNull(record.AcknowledgedAt);
        Assert.NotNull(record.SentAt);
    }

    [RequiresPostgresFact]
    public async Task EarlyAcknowledgement_ThenTimeoutSweep_DoesNotTouchExecutedCommand()
    {
        _publisher.OnPublish = async message =>
        {
            using var doc = JsonDocument.Parse(message.Payload);
            await _service.ProcessRelayStateAsync(DeviceId, StatePayload(doc.RootElement.GetProperty("commandId").GetGuid(), "ON"));
        };
        var response = await _service.SendCommandAsync(DeviceId, requestedState: true);

        var affected = await _commands.MarkTimedOutStaleAsync(DateTimeOffset.UtcNow.AddMinutes(10));

        Assert.Equal(0, affected);
        Assert.Equal("executed", (await GetAsync(response.CommandId)).Status);
    }

    [RequiresPostgresFact]
    public async Task DuplicateAcknowledgement_KeepsFirstConfirmation()
    {
        var response = await _service.SendCommandAsync(DeviceId, requestedState: true);
        await _service.ProcessRelayStateAsync(DeviceId, StatePayload(response.CommandId, "ON"));
        var first = await GetAsync(response.CommandId);

        await Task.Delay(20);
        await _service.ProcessRelayStateAsync(DeviceId, StatePayload(response.CommandId, "OFF"));

        var second = await GetAsync(response.CommandId);
        Assert.Equal("executed", second.Status);
        Assert.True(second.ActualState);
        Assert.Equal(first.AcknowledgedAt, second.AcknowledgedAt);
    }

    [RequiresPostgresFact]
    public async Task MarkExecuted_ReturnsZeroRows_WhenAlreadyExecuted()
    {
        var response = await _service.SendCommandAsync(DeviceId, requestedState: true);

        Assert.Equal(1, await _commands.MarkExecutedAsync(response.CommandId, true));
        Assert.Equal(0, await _commands.MarkExecutedAsync(response.CommandId, true));
    }

    [RequiresPostgresFact]
    public async Task Timeout_MarksStaleSentCommand_ButOnlyWhenOlderThanCutoff()
    {
        var response = await _service.SendCommandAsync(DeviceId, requestedState: true);

        Assert.Equal(0, await _commands.MarkTimedOutStaleAsync(DateTimeOffset.UtcNow.AddMinutes(-5)));
        Assert.Equal("sent", (await GetAsync(response.CommandId)).Status);

        Assert.Equal(1, await _commands.MarkTimedOutStaleAsync(DateTimeOffset.UtcNow.AddMinutes(5)));
        var timedOut = await GetAsync(response.CommandId);
        Assert.Equal("timeout", timedOut.Status);
        Assert.False(string.IsNullOrWhiteSpace(timedOut.ErrorMessage));
    }

    [RequiresPostgresFact]
    public async Task LateAcknowledgement_AfterTimeout_IsAcceptedAsExecuted()
    {
        var response = await _service.SendCommandAsync(DeviceId, requestedState: true);
        await _commands.MarkTimedOutStaleAsync(DateTimeOffset.UtcNow.AddMinutes(5));

        await _service.ProcessRelayStateAsync(DeviceId, StatePayload(response.CommandId, "ON"));

        var record = await GetAsync(response.CommandId);
        Assert.Equal("executed", record.Status);
        Assert.True(record.ActualState);
    }

    [RequiresPostgresFact]
    public async Task PublishFailure_MarksCommandFailed_AndReturnsFailedStatus()
    {
        _publisher.ExceptionToThrow = new InvalidOperationException("broker unreachable");

        var response = await _service.SendCommandAsync(DeviceId, requestedState: false);

        Assert.Equal("failed", response.Status);
        var record = await GetAsync(response.CommandId);
        Assert.Equal("failed", record.Status);
        Assert.Contains("broker unreachable", record.ErrorMessage);
    }

    [RequiresPostgresFact]
    public async Task InvalidTransitions_FromTerminalStates_AreRejected()
    {
        var executed = Guid.NewGuid();
        await _commands.InsertPendingAsync(executed, DeviceId, true, "dashboard");
        await _commands.MarkExecutedAsync(executed, true);

        await _commands.MarkSentAsync(executed, "{}");
        Assert.Equal(0, await _commands.MarkFailedAsync(executed, "late failure"));
        Assert.Equal("executed", (await GetAsync(executed)).Status);

        var failed = Guid.NewGuid();
        await _commands.InsertPendingAsync(failed, DeviceId, true, "dashboard");
        await _commands.MarkFailedAsync(failed, "boom");
        await _commands.MarkSentAsync(failed, "{}");
        Assert.Equal("failed", (await GetAsync(failed)).Status);
    }

    [RequiresPostgresFact]
    public async Task MarkSent_OnExecutedCommand_StillRecordsSentAtWithoutChangingStatus()
    {
        var id = Guid.NewGuid();
        await _commands.InsertPendingAsync(id, DeviceId, true, "dashboard");
        await _commands.MarkExecutedAsync(id, true);

        await _commands.MarkSentAsync(id, "{\"commandId\":\"" + id + "\"}");

        var record = await GetAsync(id);
        Assert.Equal("executed", record.Status);
        Assert.NotNull(record.SentAt);
    }

    [RequiresPostgresFact]
    public async Task DeviceInitiatedChange_WithoutCommandId_IsRecordedAsSyntheticExecutedRow()
    {
        await _service.ProcessRelayStateAsync(
            DeviceId, "{\"commandId\":null,\"state\":\"ON\",\"source\":\"rfid\",\"executedAt\":\"2026-07-15T16:15:55+07:00\"}");

        var history = await _commands.GetHistoryAsync(DeviceId, 10);
        var row = Assert.Single(history);
        Assert.StartsWith("rfid-", row.CommandId);
        Assert.Equal("rfid", row.Source);
        Assert.Equal("executed", row.Status);
        Assert.True(row.ActualState);
        Assert.Equal(new DateTimeOffset(2026, 7, 15, 9, 15, 55, TimeSpan.Zero), row.AcknowledgedAt);
        Assert.True((await _state.GetByDeviceIdAsync(DeviceId))!.RelayState);
    }

    [RequiresPostgresFact]
    public async Task DeviceInitiatedChange_WithoutSource_UsesUnknownSource()
    {
        await _service.ProcessRelayStateAsync(DeviceId, "{\"state\":\"OFF\"}");

        var row = Assert.Single(await _commands.GetHistoryAsync(DeviceId, 10));
        Assert.StartsWith("unknown-", row.CommandId);
        Assert.False(row.ActualState);
    }

    [RequiresPostgresFact]
    public async Task LegacyPlainTextPayload_UpdatesStateAndAuditsAsUnknown()
    {
        await _service.ProcessRelayStateAsync(DeviceId, "ON");

        Assert.True((await _state.GetByDeviceIdAsync(DeviceId))!.RelayState);
        Assert.Single(await _commands.GetHistoryAsync(DeviceId, 10));
    }

    [RequiresPostgresFact]
    public async Task NonGuidCommandId_IsIgnored_WithoutException_AndWithoutSideEffects()
    {
        await _service.ProcessRelayStateAsync(DeviceId, "{\"commandId\":\"rfid\",\"state\":\"ON\"}");

        Assert.Empty(await _commands.GetHistoryAsync(DeviceId, 10));
        Assert.Null((await _state.GetByDeviceIdAsync(DeviceId))!.RelayState);
    }

    [RequiresPostgresFact]
    public async Task UnrecognisedState_IsIgnored()
    {
        await _service.ProcessRelayStateAsync(DeviceId, "{\"state\":\"BLINK\"}");

        Assert.Empty(await _commands.GetHistoryAsync(DeviceId, 10));
    }

    [RequiresPostgresFact]
    public async Task AcknowledgementForUnknownCommandId_DoesNotCreateAnyCommandRow()
    {
        await _service.ProcessRelayStateAsync(DeviceId, StatePayload(Guid.NewGuid(), "ON"));

        Assert.Empty(await _commands.GetHistoryAsync(DeviceId, 10));
    }

    [RequiresPostgresFact]
    public async Task History_IsNewestFirst_AndRespectsLimit()
    {
        for (var i = 0; i < 3; i++)
        {
            await _service.SendCommandAsync(DeviceId, requestedState: i % 2 == 0);
        }

        var history = await _service.GetHistoryAsync(DeviceId, limit: 2);

        Assert.Equal(2, history.Count);
        Assert.True(history[0].RequestedAt >= history[1].RequestedAt);
    }
}

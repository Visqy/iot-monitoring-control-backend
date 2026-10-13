using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IotBackend.Services;
using Microsoft.Extensions.DependencyInjection;
using IotBackend.Tests.Support;
using Xunit;

namespace IotBackend.Tests.Api;

[Collection(DatabaseCollection.Name)]
public sealed class ApiWithDatabaseTests : IAsyncLifetime
{
    private ApiFactory? _factory;

    public async Task InitializeAsync()
    {
        if (!TestDatabase.IsConfigured)
        {
            return;
        }

        await TestDatabase.ResetAsync();
        _factory = new ApiFactory(TestDatabase.ConnectionString!);
    }

    public Task DisposeAsync()
    {
        _factory?.Dispose();
        return Task.CompletedTask;
    }

    private ApiFactory Factory => _factory ?? throw new InvalidOperationException(TestDatabase.SkipReason);

    [RequiresPostgresFact]
    public async Task Health_ReportsPostgresUp_ButDegradedBecauseMqttIsNotConnected()
    {
        var response = await Factory.CreateClient().GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("up", body.GetProperty("dependencies").GetProperty("postgres").GetString());
        Assert.Equal("down", body.GetProperty("dependencies").GetProperty("mqtt").GetString());
    }

    [RequiresPostgresFact]
    public async Task RelayCommand_FullHttpFlow_ReturnsAcceptedThenExecutedAfterDeviceConfirmation()
    {
        var client = await Factory.CreateAuthenticatedClientAsync();

        var post = await client.PostAsJsonAsync("/api/devices/terminal1/relay", new { state = true });

        Assert.Equal(HttpStatusCode.Accepted, post.StatusCode);
        var accepted = await post.Content.ReadFromJsonAsync<JsonElement>();
        var commandId = accepted.GetProperty("commandId").GetGuid();
        Assert.Equal("sent", accepted.GetProperty("status").GetString());
        Assert.EndsWith($"/api/commands/{commandId}", post.Headers.Location!.ToString());

        var published = Assert.Single(Factory.Publisher.Published);
        Assert.Equal("terminal1/relay/set", published.Topic);

        var beforeConfirmation = await client.GetFromJsonAsync<JsonElement>($"/api/commands/{commandId}");
        Assert.Equal("sent", beforeConfirmation.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, beforeConfirmation.GetProperty("actualState").ValueKind);

        await Factory.GetScoped(sp => sp.GetRequiredService<RelayCommandService>()
            .ProcessRelayStateAsync("terminal1", $"{{\"commandId\":\"{commandId}\",\"state\":\"ON\",\"source\":\"dashboard\"}}"));

        var afterConfirmation = await client.GetFromJsonAsync<JsonElement>($"/api/commands/{commandId}");
        Assert.Equal("executed", afterConfirmation.GetProperty("status").GetString());
        Assert.True(afterConfirmation.GetProperty("actualState").GetBoolean());
    }

    [RequiresPostgresFact]
    public async Task RelayCommand_WhenPublishFails_StillReturns202WithFailedStatus()
    {
        Factory.Publisher.ExceptionToThrow = new InvalidOperationException("broker down");
        var client = await Factory.CreateAuthenticatedClientAsync();

        var post = await client.PostAsJsonAsync("/api/devices/terminal1/relay", new { state = false });

        Assert.Equal(HttpStatusCode.Accepted, post.StatusCode);
        var body = await post.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("failed", body.GetProperty("status").GetString());
    }

    [RequiresPostgresFact]
    public async Task UnknownCommand_Returns404()
    {
        var client = await Factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync($"/api/commands/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [RequiresPostgresFact]
    public async Task DeviceState_Returns404UntilTheDeviceHasSentData_ThenExposesIt()
    {
        var client = await Factory.CreateAuthenticatedClientAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/devices/terminal1/state")).StatusCode);

        await Factory.GetScoped(sp => sp.GetRequiredService<TelemetryService>().ProcessTelemetryAsync(
            "terminal1", "terminal1/pzem", "{\"voltageB\":221.5,\"currentB\":null,\"energyB\":1.25}"));

        var state = await client.GetFromJsonAsync<JsonElement>("/api/devices/terminal1/state");
        Assert.Equal(221.5, state.GetProperty("voltageB").GetDouble());
        Assert.Equal(JsonValueKind.Null, state.GetProperty("currentB").ValueKind);
        Assert.Equal(1.25, state.GetProperty("energyB").GetDouble());
        Assert.Equal("online", state.GetProperty("status").GetString());

        var history = await client.GetFromJsonAsync<JsonElement>("/api/devices/terminal1/telemetry?limit=5");
        Assert.Equal(1, history.GetArrayLength());
    }

    [RequiresPostgresFact]
    public async Task RfidCards_CrudOverHttp_RepublishesWhitelist()
    {
        var client = await Factory.CreateAuthenticatedClientAsync();

        var created = await client.PostAsJsonAsync("/api/rfid-cards", new { uid = "04a1b2c3", label = "demo" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/rfid-cards", new { uid = "04A1B2C3" })).StatusCode);

        var patched = await client.PatchAsJsonAsync("/api/rfid-cards/04A1B2C3", new { isActive = false });
        Assert.Equal(HttpStatusCode.NoContent, patched.StatusCode);

        var cards = await client.GetFromJsonAsync<JsonElement>("/api/rfid-cards");
        var card = Assert.Single(cards.EnumerateArray());
        Assert.Equal("04A1B2C3", card.GetProperty("uid").GetString());
        Assert.False(card.GetProperty("isActive").GetBoolean());

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/rfid-cards/04A1B2C3")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync("/api/rfid-cards/04A1B2C3")).StatusCode);

        Assert.All(Factory.Publisher.Published, m =>
        {
            Assert.Equal("rfid/cards", m.Topic);
            Assert.True(m.Retain);
        });
        Assert.Equal("[]", Factory.Publisher.Published[^1].Payload);
    }
}

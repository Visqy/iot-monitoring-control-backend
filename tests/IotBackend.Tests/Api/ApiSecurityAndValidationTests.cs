using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using IotBackend.Tests.Support;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace IotBackend.Tests.Api;

/// <summary>
/// HTTP-level behaviour that does not need a working database: authentication,
/// authorization and input validation. PostgreSQL is deliberately unreachable here.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ApiSecurityAndValidationTests : IDisposable
{
    private readonly ApiFactory _factory = new(ApiFactory.UnreachablePostgres);

    public void Dispose() => _factory.Dispose();

    [Theory]
    [InlineData("GET", "/api/devices")]
    [InlineData("GET", "/api/devices/terminal1/state")]
    [InlineData("GET", "/api/devices/terminal1/telemetry")]
    [InlineData("GET", "/api/devices/terminal1/relay-commands")]
    [InlineData("GET", "/api/commands/3fa85f64-5717-4562-b3fc-2c963f66afa6")]
    [InlineData("POST", "/api/devices/terminal1/relay")]
    [InlineData("GET", "/api/rfid-cards")]
    [InlineData("GET", "/api/rfid-events")]
    [InlineData("GET", "/api/stream")]
    public async Task ProtectedEndpoints_RejectRequestsWithoutToken(string method, string path)
    {
        var client = _factory.CreateClient();

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TokenSignedWithAnotherKey_IsRejected()
    {
        var forged = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            claims: new[] { new Claim(ClaimTypes.Name, ApiFactory.Username) },
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('x', 48))), SecurityAlgorithms.HmacSha256)));
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", forged);

        var response = await client.GetAsync("/api/devices");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Health_IsPublic_AndReportsDegradedWhenDependenciesAreDown()
    {
        var response = await _factory.CreateClient().GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("degraded", body.GetProperty("status").GetString());
        Assert.Equal("down", body.GetProperty("dependencies").GetProperty("postgres").GetString());
        Assert.Equal("down", body.GetProperty("dependencies").GetProperty("mqtt").GetString());
    }

    [Fact]
    public async Task Login_WithWrongPassword_Returns401ProblemDetails()
    {
        var response = await _factory.CreateClient()
            .PostAsJsonAsync("/api/auth/login", new { username = ApiFactory.Username, password = "wrong" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Login_WithCorrectCredentials_ReturnsTokenAndExpiry()
    {
        var response = await _factory.CreateClient()
            .PostAsJsonAsync("/api/auth/login", new { username = ApiFactory.Username, password = ApiFactory.Password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiFactory.LoginBody>();
        Assert.False(string.IsNullOrWhiteSpace(body!.Token));
        Assert.True(body.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Login_WithMissingFields_Returns400()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { username = "admin" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/devices/bad@id/state")]
    [InlineData("/api/devices/bad@id/telemetry")]
    [InlineData("/api/devices/bad@id/relay-commands")]
    public async Task InvalidDeviceId_Returns400(string path)
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeviceIdLongerThan64Characters_Returns400()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/devices/" + new string('a', 65) + "/state");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RelayCommand_WithoutState_Returns400_AndPublishesNothing()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/devices/terminal1/relay", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_factory.Publisher.Published);
    }

    [Fact]
    public async Task RelayCommand_WithInvalidDeviceId_Returns400_AndPublishesNothing()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/devices/bad@id/relay", new { state = true });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_factory.Publisher.Published);
    }

    [Fact]
    public async Task CommandId_ThatIsNotAGuid_IsNotRouted()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/commands/not-a-guid");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("with space")]
    [InlineData("zz!!")]
    public async Task RfidCard_WithInvalidUid_Returns400(string uid)
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/rfid-cards", new { uid });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_factory.Publisher.Published);
    }
}

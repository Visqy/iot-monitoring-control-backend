namespace IotBackend.Contracts;

public sealed class LoginResponse
{
    public required string Token { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
}

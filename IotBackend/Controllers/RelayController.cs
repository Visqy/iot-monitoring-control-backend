using System.Text.RegularExpressions;
using IotBackend.Contracts;
using IotBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IotBackend.Controllers;

[ApiController]
[Authorize]
public sealed partial class RelayController : ControllerBase
{
    private const int DefaultHistoryLimit = 20;
    private const int MaxHistoryLimit = 100;

    private readonly RelayCommandService _relayCommandService;

    public RelayController(RelayCommandService relayCommandService)
    {
        _relayCommandService = relayCommandService;
    }

    [HttpPost("api/devices/{deviceId}/relay")]
    public async Task<IActionResult> PostRelay(
        string deviceId, [FromBody] RelayCommandRequest request, CancellationToken cancellationToken)
    {
        if (!DeviceIdFormatRegex().IsMatch(deviceId))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "deviceId tidak valid.");
        }

        var response = await _relayCommandService.SendCommandAsync(deviceId, request.State, cancellationToken);
        return Accepted($"/api/commands/{response.CommandId}", response);
    }

    [HttpGet("api/commands/{commandId:guid}")]
    public async Task<IActionResult> GetCommand(Guid commandId, CancellationToken cancellationToken)
    {
        var command = await _relayCommandService.GetCommandAsync(commandId, cancellationToken);
        if (command is null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound,
                title: $"Command '{commandId}' tidak ditemukan.");
        }

        return Ok(command);
    }

    [HttpGet("api/devices/{deviceId}/relay-commands")]
    public async Task<IActionResult> GetHistory(
        string deviceId, [FromQuery] int? limit, CancellationToken cancellationToken)
    {
        if (!DeviceIdFormatRegex().IsMatch(deviceId))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "deviceId tidak valid.");
        }

        var effectiveLimit = limit is null or <= 0 ? DefaultHistoryLimit : Math.Min(limit.Value, MaxHistoryLimit);

        var history = await _relayCommandService.GetHistoryAsync(deviceId, effectiveLimit, cancellationToken);
        return Ok(history);
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex DeviceIdFormatRegex();
}

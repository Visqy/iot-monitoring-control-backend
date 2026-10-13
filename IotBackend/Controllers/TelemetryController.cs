using System.Text.RegularExpressions;
using IotBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IotBackend.Controllers;

[ApiController]
[Route("api/devices/{deviceId}/telemetry")]
[Authorize]
public sealed partial class TelemetryController : ControllerBase
{
    private const int DefaultLimit = 100;
    private const int MaxLimit = 1000;

    private readonly TelemetryService _telemetryService;

    public TelemetryController(TelemetryService telemetryService)
    {
        _telemetryService = telemetryService;
    }

    [HttpGet]
    public async Task<IActionResult> GetHistory(
        string deviceId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        if (!DeviceIdFormatRegex().IsMatch(deviceId))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "deviceId tidak valid.");
        }

        var effectiveLimit = limit is null or <= 0 ? DefaultLimit : Math.Min(limit.Value, MaxLimit);

        var history = await _telemetryService.GetHistoryAsync(deviceId, from, to, effectiveLimit, cancellationToken);
        return Ok(history);
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex DeviceIdFormatRegex();
}

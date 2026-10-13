using System.Text.RegularExpressions;
using IotBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IotBackend.Controllers;

[ApiController]
[Route("api/devices")]
[Authorize]
public sealed partial class DevicesController : ControllerBase
{
    private readonly DeviceService _deviceService;

    public DevicesController(DeviceService deviceService)
    {
        _deviceService = deviceService;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var devices = await _deviceService.ListDevicesAsync(cancellationToken);
        return Ok(devices);
    }

    [HttpGet("{deviceId}/state")]
    public async Task<IActionResult> GetState(string deviceId, CancellationToken cancellationToken)
    {
        if (!DeviceIdFormatRegex().IsMatch(deviceId))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "deviceId tidak valid.");
        }

        var state = await _deviceService.GetDeviceStateAsync(deviceId, cancellationToken);
        if (state is null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound,
                title: $"Device '{deviceId}' belum pernah mengirim data.");
        }

        return Ok(state);
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex DeviceIdFormatRegex();
}

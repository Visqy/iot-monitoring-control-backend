using System.Text.RegularExpressions;
using IotBackend.Contracts;
using IotBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IotBackend.Controllers;

[ApiController]
[Authorize]
public sealed partial class RfidController : ControllerBase
{
    private const int DefaultEventLimit = 50;
    private const int MaxEventLimit = 200;

    private readonly RfidService _rfidService;

    public RfidController(RfidService rfidService)
    {
        _rfidService = rfidService;
    }

    [HttpGet("api/rfid-cards")]
    public async Task<IActionResult> ListCards(CancellationToken cancellationToken)
    {
        var cards = await _rfidService.ListCardsAsync(cancellationToken);
        return Ok(cards);
    }

    [HttpPost("api/rfid-cards")]
    public async Task<IActionResult> AddCard([FromBody] CreateRfidCardRequest request, CancellationToken cancellationToken)
    {
        if (!UidFormatRegex().IsMatch(request.Uid))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "uid tidak valid.");
        }

        var added = await _rfidService.AddCardAsync(request.Uid, request.Label, cancellationToken);
        if (!added)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: $"Kartu '{request.Uid}' sudah terdaftar.");
        }

        return Created($"/api/rfid-cards/{request.Uid}", null);
    }

    [HttpPatch("api/rfid-cards/{uid}")]
    public async Task<IActionResult> UpdateCard(
        string uid, [FromBody] UpdateRfidCardRequest request, CancellationToken cancellationToken)
    {
        if (!UidFormatRegex().IsMatch(uid))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "uid tidak valid.");
        }

        var updated = await _rfidService.UpdateCardAsync(uid, request.IsActive, request.Label, cancellationToken);
        if (!updated)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: $"Kartu '{uid}' tidak ditemukan.");
        }

        return NoContent();
    }

    [HttpDelete("api/rfid-cards/{uid}")]
    public async Task<IActionResult> DeleteCard(string uid, CancellationToken cancellationToken)
    {
        if (!UidFormatRegex().IsMatch(uid))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "uid tidak valid.");
        }

        var deleted = await _rfidService.DeleteCardAsync(uid, cancellationToken);
        if (!deleted)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: $"Kartu '{uid}' tidak ditemukan.");
        }

        return NoContent();
    }

    [HttpGet("api/rfid-events")]
    public async Task<IActionResult> GetEvents([FromQuery] int? limit, CancellationToken cancellationToken)
    {
        var effectiveLimit = limit is null or <= 0 ? DefaultEventLimit : Math.Min(limit.Value, MaxEventLimit);

        var events = await _rfidService.GetEventHistoryAsync(effectiveLimit, cancellationToken);
        return Ok(events);
    }

    [GeneratedRegex("^[A-Za-z0-9]{1,64}$")]
    private static partial Regex UidFormatRegex();
}

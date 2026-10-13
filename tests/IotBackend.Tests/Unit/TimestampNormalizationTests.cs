using IotBackend.Services;
using Xunit;

namespace IotBackend.Tests.Unit;

public class TimestampNormalizationTests
{
    [Fact]
    public void LegacyLocalFormat_IsTreatedAsWib_AndConvertedToUtc()
    {
        var result = TelemetryService.ParseDeviceTimestamp("2026-07-23 13:51:10");

        Assert.Equal(new DateTime(2026, 7, 23, 6, 51, 10, DateTimeKind.Utc), result);
        Assert.Equal(DateTimeKind.Utc, result!.Value.Kind);
    }

    [Fact]
    public void Iso8601WithOffset_IsConvertedToUtc()
    {
        var result = TelemetryService.ParseDeviceTimestamp("2026-07-23T13:51:10+07:00");

        Assert.Equal(new DateTime(2026, 7, 23, 6, 51, 10, DateTimeKind.Utc), result);
    }

    [Fact]
    public void Iso8601InUtc_IsKept()
    {
        var result = TelemetryService.ParseDeviceTimestamp("2026-07-23T06:51:10Z");

        Assert.Equal(new DateTime(2026, 7, 23, 6, 51, 10, DateTimeKind.Utc), result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a timestamp")]
    public void MissingOrInvalidTimestamp_ReturnsNull(string? raw)
    {
        Assert.Null(TelemetryService.ParseDeviceTimestamp(raw));
    }

    [Fact]
    public void ExecutedAt_WithNonZeroOffset_IsNormalizedToZeroOffset()
    {
        var result = RelayCommandService.ParseExecutedAt("2026-07-15T16:15:55+07:00");

        Assert.NotNull(result);
        Assert.Equal(TimeSpan.Zero, result!.Value.Offset);
        Assert.Equal(new DateTimeOffset(2026, 7, 15, 9, 15, 55, TimeSpan.Zero), result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    public void ExecutedAt_MissingOrInvalid_ReturnsNull(string? raw)
    {
        Assert.Null(RelayCommandService.ParseExecutedAt(raw));
    }
}

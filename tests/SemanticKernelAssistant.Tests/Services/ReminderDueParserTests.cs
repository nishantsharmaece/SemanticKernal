using SemanticKernelAssistant.Web.Services;

namespace SemanticKernelAssistant.Tests.Services;

public sealed class ReminderDueParserTests
{
    private static readonly FakeClock Clock = new(
        new DateTimeOffset(2026, 9, 4, 18, 0, 0, TimeSpan.FromHours(5.5)));

    [Fact]
    public void ParsesRelativeMinutes()
    {
        Assert.True(ReminderDueParser.TryParse("in 10 minutes", Clock, out var dueAtUtc, out _));
        Assert.Equal(Clock.UtcNow.AddMinutes(10), dueAtUtc);
    }

    [Fact]
    public void ParsesTomorrowMorning()
    {
        Assert.True(ReminderDueParser.TryParse("tomorrow 9am", Clock, out var dueAtUtc, out _));
        var expectedLocal = new DateTimeOffset(2026, 9, 5, 9, 0, 0, Clock.Now.Offset);
        Assert.Equal(expectedLocal.UtcDateTime, dueAtUtc);
    }

    [Fact]
    public void RejectsPastTimes()
    {
        Assert.False(ReminderDueParser.TryParse("today 9am", Clock, out _, out var error));
        Assert.Contains("future", error);
    }

    [Fact]
    public void ParsesIsoLocalTime()
    {
        Assert.True(
            ReminderDueParser.TryParse("2026-09-05T15:30:00", Clock, out var dueAtUtc, out _));
        Assert.Equal(new DateTime(2026, 9, 5, 10, 0, 0, DateTimeKind.Utc), dueAtUtc);
    }

    [Fact]
    public void ParsesLocalDateAndTimeAsIndiaStandardTime()
    {
        Assert.True(
            ReminderDueParser.TryParse("September 5, 2026 3:30 PM", Clock, out var dueAtUtc, out _));
        Assert.Equal(new DateTime(2026, 9, 5, 10, 0, 0, DateTimeKind.Utc), dueAtUtc);
    }

    [Fact]
    public void PreservesExplicitIsoOffset()
    {
        Assert.True(
            ReminderDueParser.TryParse(
                "2026-09-05T15:30:00+02:00",
                Clock,
                out var dueAtUtc,
                out _));
        Assert.Equal(new DateTime(2026, 9, 5, 13, 30, 0, DateTimeKind.Utc), dueAtUtc);
    }
}

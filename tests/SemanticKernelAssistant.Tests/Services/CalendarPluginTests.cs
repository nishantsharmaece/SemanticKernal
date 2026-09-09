using SemanticKernelAssistant.Web.Models;
using SemanticKernelAssistant.Web.Plugins;
using SemanticKernelAssistant.Web.Services;

namespace SemanticKernelAssistant.Tests.Services;

public sealed class CalendarPluginTests
{
    [Fact]
    public async Task CreateMeetingInviteReturnsErrorWhenGoogleIsNotConnected()
    {
        var plugin = CreatePlugin(new DisconnectedCalendarInviteService());

        var result = await plugin.CreateMeetingInviteAsync(
            "Project review",
            "tomorrow 10am",
            30,
            "bob@gmail.com");

        Assert.Contains("error", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not connected", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Invitation sent", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateMeetingInviteRejectsMissingAtSignInAttendeeEmails()
    {
        var plugin = CreatePlugin(new DisconnectedCalendarInviteService());

        var result = await plugin.CreateMeetingInviteAsync(
            "Project review",
            "tomorrow 10am",
            30,
            "not-an-email");

        Assert.Contains("error", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("email", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Invitation sent", result, StringComparison.Ordinal);
    }

    private static CalendarPlugin CreatePlugin(ICalendarInviteService calendarInviteService)
    {
        var clock = new FakeClock(
            new DateTimeOffset(2026, 9, 6, 11, 0, 0, TimeSpan.FromHours(5.5)));
        return new CalendarPlugin(calendarInviteService, clock);
    }

    private sealed class DisconnectedCalendarInviteService : ICalendarInviteService
    {
        public Task<CalendarInviteResult> CreateInviteAsync(
            string subject,
            DateTime startUtc,
            int durationMinutes,
            IReadOnlyList<string> attendeeEmails,
            string? description,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException(GoogleConnection.NotConnectedMessage);
        }

        public Task<IReadOnlyList<CalendarEventSummary>> ListTodayAsync(
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException(GoogleConnection.NotConnectedMessage);
        }
    }
}

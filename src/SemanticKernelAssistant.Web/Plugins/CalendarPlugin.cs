using System.ComponentModel;
using System.Text.Json;
using Microsoft.SemanticKernel;
using SemanticKernelAssistant.Web.Services;

namespace SemanticKernelAssistant.Web.Plugins;

public sealed class CalendarPlugin(ICalendarInviteService calendarInviteService, IClock clock)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [KernelFunction]
    [Description("Create a Google Calendar event and email invitations to attendees via Gmail/Google Calendar. Use when the user asks to invite someone, send a meeting invite, schedule a meeting, or create a calendar invitation. Do not use this for local reminders or alarms.")]
    public async Task<string> CreateMeetingInviteAsync(
        [Description("Meeting title, such as 'Project review'.")]
        string subject,
        [Description("When the meeting starts. Prefer ISO 8601 like 2026-09-07T10:00:00, or a relative phrase such as 'tomorrow 10am'.")]
        string startAt,
        [Description("Meeting length in minutes. Defaults to 30.")]
        int durationMinutes = 30,
        [Description("Comma-separated attendee email addresses.")]
        string attendeeEmails = "",
        [Description("Optional meeting description sent to guests.")]
        string? description = null)
    {
        if (durationMinutes <= 0)
        {
            durationMinutes = 30;
        }

        var emails = SplitEmails(attendeeEmails);
        if (emails.Count == 0)
        {
            return JsonSerializer.Serialize(
                new { error = "At least one attendee email is required." },
                JsonOptions);
        }

        if (!ReminderDueParser.TryParse(startAt, clock, out var startUtc, out var error))
        {
            return JsonSerializer.Serialize(new { error }, JsonOptions);
        }

        try
        {
            var invite = await calendarInviteService.CreateInviteAsync(
                subject,
                startUtc,
                durationMinutes,
                emails,
                description,
                CancellationToken.None);
            return JsonSerializer.Serialize(
                new
                {
                    invite.Id,
                    invite.Subject,
                    startUtc = invite.StartUtc,
                    endUtc = invite.EndUtc,
                    attendees = invite.Attendees,
                    htmlLink = invite.HtmlLink,
                    message = "Invitation sent via Google Calendar."
                },
                JsonOptions);
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            return JsonSerializer.Serialize(new { error = exception.Message }, JsonOptions);
        }
    }

    [KernelFunction]
    [Description("List today's Google Calendar events. Use when the user asks what meetings they have today.")]
    public async Task<string> ListTodayEventsAsync()
    {
        try
        {
            var events = await calendarInviteService.ListTodayAsync(CancellationToken.None);
            return JsonSerializer.Serialize(events, JsonOptions);
        }
        catch (InvalidOperationException exception)
        {
            return JsonSerializer.Serialize(new { error = exception.Message }, JsonOptions);
        }
    }

    private static List<string> SplitEmails(string? attendeeEmails)
    {
        if (string.IsNullOrWhiteSpace(attendeeEmails))
        {
            return [];
        }

        return attendeeEmails
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(email => email.Contains('@'))
            .ToList();
    }
}

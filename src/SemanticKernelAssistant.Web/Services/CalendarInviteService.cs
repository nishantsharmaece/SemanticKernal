using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SemanticKernelAssistant.Web.Models;

namespace SemanticKernelAssistant.Web.Services;

public interface ICalendarInviteService
{
    Task<CalendarInviteResult> CreateInviteAsync(
        string subject,
        DateTime startUtc,
        int durationMinutes,
        IReadOnlyList<string> attendeeEmails,
        string? description,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CalendarEventSummary>> ListTodayAsync(CancellationToken cancellationToken);
}

public sealed class CalendarInviteService(
    IGoogleConnection googleConnection,
    IHttpClientFactory httpClientFactory,
    IClock clock) : ICalendarInviteService
{
    public async Task<CalendarInviteResult> CreateInviteAsync(
        string subject,
        DateTime startUtc,
        int durationMinutes,
        IReadOnlyList<string> attendeeEmails,
        string? description,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new ArgumentException("A meeting subject is required.", nameof(subject));
        }

        if (durationMinutes <= 0)
        {
            throw new ArgumentException("Duration must be greater than zero.", nameof(durationMinutes));
        }

        var attendees = attendeeEmails
            .Where(email => !string.IsNullOrWhiteSpace(email) && email.Contains('@'))
            .Select(email => email.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (attendees.Count == 0)
        {
            throw new ArgumentException("At least one attendee email is required.", nameof(attendeeEmails));
        }

        var accessToken = await RequireAccessTokenAsync(cancellationToken);
        var timeZone = GetLocalTimeZoneId();
        var startLocal = ToLocalUnspecified(startUtc);
        var endLocal = startLocal.AddMinutes(durationMinutes);
        var payload = new
        {
            summary = subject.Trim(),
            description,
            start = new { dateTime = FormatLocal(startLocal), timeZone },
            end = new { dateTime = FormatLocal(endLocal), timeZone },
            attendees = attendees.Select(email => new { email }).ToArray()
        };

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "calendars/primary/events?sendUpdates=all")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await SendAsync(request, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var id = root.TryGetProperty("id", out var idElement)
            ? idElement.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new InvalidOperationException("Google Calendar request failed.");
        }

        var htmlLink = root.TryGetProperty("htmlLink", out var linkElement)
            ? linkElement.GetString()
            : null;
        var start = ParseEventTime(root, "start") ?? startUtc;
        var end = ParseEventTime(root, "end") ?? startUtc.AddMinutes(durationMinutes);
        var returnedAttendees = ParseAttendees(root);
        if (returnedAttendees.Count == 0)
        {
            returnedAttendees = attendees;
        }

        return new CalendarInviteResult(
            id,
            root.TryGetProperty("summary", out var summaryElement)
                ? summaryElement.GetString() ?? subject.Trim()
                : subject.Trim(),
            start,
            end,
            returnedAttendees,
            htmlLink);
    }

    public async Task<IReadOnlyList<CalendarEventSummary>> ListTodayAsync(
        CancellationToken cancellationToken)
    {
        var accessToken = await RequireAccessTokenAsync(cancellationToken);
        var start = new DateTimeOffset(clock.Now.Date, clock.Now.Offset);
        var end = start.AddDays(1);
        var query = QueryString.Create(
            new Dictionary<string, string?>
            {
                ["timeMin"] = start.UtcDateTime.ToString("o", CultureInfo.InvariantCulture),
                ["timeMax"] = end.UtcDateTime.ToString("o", CultureInfo.InvariantCulture),
                ["singleEvents"] = "true",
                ["orderBy"] = "startTime"
            });

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "calendars/primary/events" + query);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await SendAsync(request, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("items", out var items)
            || items.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var events = new List<CalendarEventSummary>();
        foreach (var item in items.EnumerateArray())
        {
            var id = item.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var subject = item.TryGetProperty("summary", out var summaryElement)
                ? summaryElement.GetString() ?? "(No title)"
                : "(No title)";
            var location = item.TryGetProperty("location", out var locationElement)
                ? locationElement.GetString()
                : null;
            var startUtc = ParseEventTime(item, "start") ?? clock.UtcNow;
            var endUtc = ParseEventTime(item, "end") ?? startUtc;

            events.Add(new CalendarEventSummary(id, subject, startUtc, endUtc, location));
        }

        return events;
    }

    private async Task<string> RequireAccessTokenAsync(CancellationToken cancellationToken)
    {
        var accessToken = await googleConnection.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new InvalidOperationException(GoogleConnection.NotConnectedMessage);
        }

        return accessToken;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await httpClientFactory
            .CreateClient("GoogleCalendar")
            .SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();
            throw new InvalidOperationException("Google Calendar request failed.");
        }

        return response;
    }

    private static string GetLocalTimeZoneId()
    {
        var windowsId = TimeZoneInfo.Local.Id;
        return TimeZoneInfo.TryConvertWindowsIdToIanaId(windowsId, out var ianaId)
            && !string.IsNullOrWhiteSpace(ianaId)
            ? ianaId
            : windowsId;
    }

    private static DateTime ToLocalUnspecified(DateTime startUtc)
    {
        var utc = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc);
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.Local);
        return DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
    }

    private static string FormatLocal(DateTime local) =>
        local.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);

    private static DateTime? ParseEventTime(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var container))
        {
            return null;
        }

        if (container.TryGetProperty("dateTime", out var dateTimeElement))
        {
            var text = dateTimeElement.GetString();
            if (DateTimeOffset.TryParse(
                    text,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var parsed))
            {
                return parsed.UtcDateTime;
            }
        }

        if (container.TryGetProperty("date", out var dateElement)
            && DateTime.TryParse(
                dateElement.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var date))
        {
            return date;
        }

        return null;
    }

    private static IReadOnlyList<string> ParseAttendees(JsonElement root)
    {
        if (!root.TryGetProperty("attendees", out var attendees)
            || attendees.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return attendees
            .EnumerateArray()
            .Select(item => item.TryGetProperty("email", out var email) ? email.GetString() : null)
            .Where(email => !string.IsNullOrWhiteSpace(email))
            .Select(email => email!)
            .ToList();
    }
}

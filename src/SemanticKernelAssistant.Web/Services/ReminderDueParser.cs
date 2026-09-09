using System.Globalization;
using System.Text.RegularExpressions;

namespace SemanticKernelAssistant.Web.Services;

public static class ReminderDueParser
{
    private static readonly Regex RelativePattern = new(
        @"^(?:in\s+)?(?<amount>\d+)\s*(?<unit>minutes?|mins?|hours?|hrs?|days?)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex TomorrowPattern = new(
        @"^tomorrow(?:\s+at)?(?:\s+(?<hour>\d{1,2})(?::(?<minute>\d{2}))?\s*(?<meridiem>am|pm)?)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex TodayPattern = new(
        @"^(?:today\s+(?:at\s+)?)?(?<hour>\d{1,2})(?::(?<minute>\d{2}))?\s*(?<meridiem>am|pm)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool TryParse(
        string? value,
        IClock clock,
        out DateTime dueAtUtc,
        out string error)
    {
        dueAtUtc = default;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            error = "A due time is required.";
            return false;
        }

        var input = value.Trim();

        if (TryParseRelative(input, clock, out dueAtUtc)
            || TryParseTomorrow(input, clock, out dueAtUtc)
            || TryParseClockTime(input, clock, out dueAtUtc)
            || TryParseAbsolute(input, out dueAtUtc))
        {
            if (dueAtUtc <= clock.UtcNow)
            {
                error = "The due time must be in the future.";
                dueAtUtc = default;
                return false;
            }

            return true;
        }

        error = "Could not understand that due time. Use ISO 8601 or a phrase such as 'in 10 minutes'.";
        return false;
    }

    private static bool TryParseRelative(string input, IClock clock, out DateTime dueAtUtc)
    {
        dueAtUtc = default;
        var match = RelativePattern.Match(input);
        if (!match.Success)
        {
            return false;
        }

        var amount = int.Parse(match.Groups["amount"].Value, CultureInfo.InvariantCulture);
        var unit = match.Groups["unit"].Value.ToLowerInvariant();
        var offset = unit.StartsWith("day", StringComparison.Ordinal)
            ? TimeSpan.FromDays(amount)
            : unit.StartsWith("hour", StringComparison.Ordinal) || unit.StartsWith("hr", StringComparison.Ordinal)
                ? TimeSpan.FromHours(amount)
                : TimeSpan.FromMinutes(amount);

        dueAtUtc = clock.UtcNow.Add(offset);
        return true;
    }

    private static bool TryParseTomorrow(string input, IClock clock, out DateTime dueAtUtc)
    {
        dueAtUtc = default;
        var match = TomorrowPattern.Match(input);
        if (!match.Success)
        {
            return false;
        }

        var local = clock.Now.Date.AddDays(1);
        if (match.Groups["hour"].Success)
        {
            local = CombineTime(local, match);
        }
        else
        {
            local = local.AddHours(9);
        }

        dueAtUtc = IndiaTimeZone.ToUtc(local);
        return true;
    }

    private static bool TryParseClockTime(string input, IClock clock, out DateTime dueAtUtc)
    {
        dueAtUtc = default;
        var match = TodayPattern.Match(input);
        if (!match.Success)
        {
            return false;
        }

        var local = CombineTime(clock.Now.Date, match);
        dueAtUtc = IndiaTimeZone.ToUtc(local);
        return true;
    }

    private static bool TryParseAbsolute(string input, out DateTime dueAtUtc)
    {
        dueAtUtc = default;

        if (HasExplicitOffset(input)
            && (DateTimeOffset.TryParse(
                    input,
                    CultureInfo.CurrentCulture,
                    DateTimeStyles.AllowWhiteSpaces,
                    out var offsetValue)
                || DateTimeOffset.TryParse(
                    input,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces,
                    out offsetValue)))
        {
            dueAtUtc = offsetValue.UtcDateTime;
            return true;
        }

        if (DateTime.TryParse(
                input,
                CultureInfo.CurrentCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var localValue)
            || DateTime.TryParse(
                input,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out localValue))
        {
            dueAtUtc = IndiaTimeZone.ToUtc(localValue);
            return true;
        }

        return false;
    }

    private static bool HasExplicitOffset(string input)
    {
        return Regex.IsMatch(
            input,
            @"(?:Z|[+-]\d{2}:\d{2})\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static DateTime CombineTime(DateTime date, Match match)
    {
        var hour = int.Parse(match.Groups["hour"].Value, CultureInfo.InvariantCulture);
        var minute = match.Groups["minute"].Success
            ? int.Parse(match.Groups["minute"].Value, CultureInfo.InvariantCulture)
            : 0;

        if (match.Groups["meridiem"].Success)
        {
            var meridiem = match.Groups["meridiem"].Value.ToLowerInvariant();
            hour %= 12;
            if (meridiem == "pm")
            {
                hour += 12;
            }
        }

        return date.AddHours(hour).AddMinutes(minute);
    }

}

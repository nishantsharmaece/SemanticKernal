namespace SemanticKernelAssistant.Web.Services;

public interface IClock
{
    DateTime UtcNow { get; }

    DateTimeOffset Now { get; }
}

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;

    public DateTimeOffset Now => IndiaTimeZone.ToIndiaTime(DateTimeOffset.UtcNow);
}

public static class IndiaTimeZone
{
    public const string IanaId = "Asia/Kolkata";

    private static readonly TimeZoneInfo TimeZone = FindTimeZone();

    public static DateTimeOffset ToIndiaTime(DateTimeOffset value)
    {
        return TimeZoneInfo.ConvertTime(value, TimeZone);
    }

    public static DateTimeOffset ToIndiaTime(DateTime utcValue)
    {
        var utc = new DateTimeOffset(DateTime.SpecifyKind(utcValue, DateTimeKind.Utc));
        return ToIndiaTime(utc);
    }

    public static DateTime ToUtc(DateTime localTime)
    {
        var unspecified = DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, TimeZone);
    }

    private static TimeZoneInfo FindTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(IanaId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        }
    }
}

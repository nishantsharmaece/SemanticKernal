using SemanticKernelAssistant.Web.Data;
using SemanticKernelAssistant.Web.Models;

namespace SemanticKernelAssistant.Web.Services;

public sealed class BriefingService(IReminderStore reminderStore, IClock clock) : IBriefingService
{
    public async Task<DailyBriefingResponse> GetTodayAsync(CancellationToken cancellationToken)
    {
        var now = clock.Now;
        var today = now.Date;
        var reminders = await reminderStore.ListAsync(cancellationToken);

        var dueToday = reminders
            .Where(item => ToLocalDate(item.DueAtUtc, now.Offset) == today)
            .ToList();
        var overdue = reminders
            .Where(item =>
                item.Status is ReminderStatuses.Pending or ReminderStatuses.Fired
                && ToLocalDate(item.DueAtUtc, now.Offset) < today)
            .ToList();

        return new DailyBriefingResponse(
            clock.UtcNow,
            today.ToString("yyyy-MM-dd"),
            reminders.Count(item => item.Status == ReminderStatuses.Pending),
            dueToday.Count,
            overdue.Count,
            dueToday,
            overdue);
    }

    private static DateTime ToLocalDate(DateTime dueAtUtc, TimeSpan offset)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(dueAtUtc, DateTimeKind.Utc))
            .ToOffset(offset)
            .Date;
    }
}

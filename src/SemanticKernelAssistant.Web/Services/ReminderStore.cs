using Microsoft.EntityFrameworkCore;
using SemanticKernelAssistant.Web.Data;
using SemanticKernelAssistant.Web.Models;

namespace SemanticKernelAssistant.Web.Services;

public interface IReminderStore
{
    Task<Reminder> CreateAsync(
        string title,
        DateTime dueAtUtc,
        string? notes,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ReminderResponse>> ListAsync(CancellationToken cancellationToken);

    Task<Reminder?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<Reminder> CompleteAsync(Guid id, CancellationToken cancellationToken);

    Task<Reminder> CancelAsync(Guid id, CancellationToken cancellationToken);

    Task<int> FireDueAsync(CancellationToken cancellationToken);
}

public sealed class ReminderStore(AssistantDbContext dbContext, IClock clock) : IReminderStore
{
    public const int MaximumTitleLength = 200;
    public const int MaximumNotesLength = 2_000;

    public async Task<Reminder> CreateAsync(
        string title,
        DateTime dueAtUtc,
        string? notes,
        CancellationToken cancellationToken)
    {
        var reminder = new Reminder
        {
            Title = NormalizeTitle(title),
            Notes = NormalizeNotes(notes),
            DueAtUtc = DateTime.SpecifyKind(dueAtUtc, DateTimeKind.Utc),
            Status = ReminderStatuses.Pending,
            CreatedAtUtc = clock.UtcNow
        };

        dbContext.Reminders.Add(reminder);
        await dbContext.SaveChangesAsync(cancellationToken);
        return reminder;
    }

    public async Task<IReadOnlyList<ReminderResponse>> ListAsync(
        CancellationToken cancellationToken)
    {
        var cutoff = clock.UtcNow.AddDays(-1);
        return await dbContext.Reminders
            .AsNoTracking()
            .Where(item =>
                item.Status == ReminderStatuses.Pending
                || (item.Status == ReminderStatuses.Fired && item.FiredAtUtc >= cutoff))
            .OrderBy(item => item.DueAtUtc)
            .Select(item => new ReminderResponse(
                item.Id,
                item.Title,
                item.Notes,
                item.DueAtUtc,
                item.Status,
                item.CreatedAtUtc,
                item.FiredAtUtc))
            .ToListAsync(cancellationToken);
    }

    public Task<Reminder?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        return dbContext.Reminders.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
    }

    public async Task<Reminder> CompleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var reminder = await GetRequiredAsync(id, cancellationToken);
        reminder.Status = ReminderStatuses.Completed;
        await dbContext.SaveChangesAsync(cancellationToken);
        return reminder;
    }

    public async Task<Reminder> CancelAsync(Guid id, CancellationToken cancellationToken)
    {
        var reminder = await GetRequiredAsync(id, cancellationToken);
        if (reminder.Status is ReminderStatuses.Completed or ReminderStatuses.Cancelled)
        {
            return reminder;
        }

        reminder.Status = ReminderStatuses.Cancelled;
        await dbContext.SaveChangesAsync(cancellationToken);
        return reminder;
    }

    public async Task<int> FireDueAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var due = await dbContext.Reminders
            .Where(item => item.Status == ReminderStatuses.Pending && item.DueAtUtc <= now)
            .ToListAsync(cancellationToken);

        foreach (var reminder in due)
        {
            reminder.Status = ReminderStatuses.Fired;
            reminder.FiredAtUtc = now;
        }

        if (due.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return due.Count;
    }

    private async Task<Reminder> GetRequiredAsync(Guid id, CancellationToken cancellationToken)
    {
        return await GetAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException("Reminder was not found.");
    }

    private static string NormalizeTitle(string title)
    {
        var value = title.Trim();
        if (value.Length == 0)
        {
            throw new ArgumentException("A reminder title is required.", nameof(title));
        }

        return value.Length <= MaximumTitleLength ? value : value[..MaximumTitleLength];
    }

    private static string? NormalizeNotes(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return null;
        }

        var value = notes.Trim();
        return value.Length <= MaximumNotesLength ? value : value[..MaximumNotesLength];
    }
}

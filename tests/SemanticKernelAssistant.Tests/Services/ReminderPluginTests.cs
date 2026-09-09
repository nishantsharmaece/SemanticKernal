using SemanticKernelAssistant.Tests.Data;
using SemanticKernelAssistant.Web.Data;
using SemanticKernelAssistant.Web.Plugins;
using SemanticKernelAssistant.Web.Services;

namespace SemanticKernelAssistant.Tests.Services;

public sealed class ReminderPluginTests
{
    [Fact]
    public async Task CreateListAndCompleteReminder()
    {
        await using var database = await TestDatabase.CreateAsync();
        var clock = new FakeClock(
            new DateTimeOffset(2026, 9, 4, 18, 0, 0, TimeSpan.FromHours(5.5)));
        var store = new ReminderStore(database.Context, clock);
        var plugin = new ReminderPlugin(store, clock);

        var createdJson = await plugin.CreateReminderAsync("Stretch", "in 10 minutes");
        Assert.Contains("Stretch", createdJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"error\"", createdJson, StringComparison.Ordinal);

        var listedJson = await plugin.ListRemindersAsync();
        Assert.Contains("Stretch", listedJson, StringComparison.Ordinal);

        var reminder = (await store.ListAsync(CancellationToken.None)).Single();
        var completedJson = await plugin.CompleteReminderAsync(reminder.Id.ToString());
        Assert.Contains(ReminderStatuses.Completed, completedJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateRejectsEmptyTitle()
    {
        await using var database = await TestDatabase.CreateAsync();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var plugin = new ReminderPlugin(new ReminderStore(database.Context, clock), clock);

        var result = await plugin.CreateReminderAsync("   ", "in 5 minutes");
        Assert.Contains("error", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FireDueMarksPendingReminders()
    {
        await using var database = await TestDatabase.CreateAsync();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var store = new ReminderStore(database.Context, clock);
        await store.CreateAsync(
            "Check oven",
            clock.UtcNow.AddMinutes(-1),
            null,
            CancellationToken.None);

        var fired = await store.FireDueAsync(CancellationToken.None);
        var reminders = await store.ListAsync(CancellationToken.None);

        Assert.Equal(1, fired);
        Assert.Equal(ReminderStatuses.Fired, reminders[0].Status);
    }
}

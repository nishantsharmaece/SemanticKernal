using System.Text.Json;
using SemanticKernelAssistant.Tests.Data;
using SemanticKernelAssistant.Web.Data;
using SemanticKernelAssistant.Web.Plugins;
using SemanticKernelAssistant.Web.Services;

namespace SemanticKernelAssistant.Tests.Services;

public sealed class BriefingPluginTests
{
    [Fact]
    public async Task GetTodayIncludesDueTodayAndOverdueReminders()
    {
        await using var database = await TestDatabase.CreateAsync();
        var clock = new FakeClock(
            new DateTimeOffset(2026, 9, 5, 10, 0, 0, TimeSpan.FromHours(5.5)));
        var store = new ReminderStore(database.Context, clock);
        var briefing = new BriefingService(store, clock);
        var plugin = new BriefingPlugin(briefing);

        await store.CreateAsync(
            "Standup",
            new DateTimeOffset(2026, 9, 5, 11, 0, 0, clock.Now.Offset).UtcDateTime,
            null,
            CancellationToken.None);
        await store.CreateAsync(
            "Yesterday task",
            clock.UtcNow.AddDays(-1),
            null,
            CancellationToken.None);
        await store.CreateAsync(
            "Next week",
            clock.UtcNow.AddDays(7),
            null,
            CancellationToken.None);

        var json = await plugin.GetDailyBriefingAsync();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("2026-09-05", root.GetProperty("localDate").GetString());
        Assert.Equal(3, root.GetProperty("pendingCount").GetInt32());
        Assert.Equal(1, root.GetProperty("dueTodayCount").GetInt32());
        Assert.Equal(1, root.GetProperty("overdueCount").GetInt32());
        Assert.Equal("Standup", root.GetProperty("dueToday")[0].GetProperty("title").GetString());
        Assert.Equal("Yesterday task", root.GetProperty("overdue")[0].GetProperty("title").GetString());
        Assert.DoesNotContain("Next week", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetTodayReturnsEmptyListsWhenNoRemindersExist()
    {
        await using var database = await TestDatabase.CreateAsync();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var plugin = new BriefingPlugin(
            new BriefingService(new ReminderStore(database.Context, clock), clock));

        var json = await plugin.GetDailyBriefingAsync();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(0, root.GetProperty("pendingCount").GetInt32());
        Assert.Equal(0, root.GetProperty("dueToday").GetArrayLength());
        Assert.Equal(0, root.GetProperty("overdue").GetArrayLength());
    }
}

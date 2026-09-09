using System.ComponentModel;
using System.Text.Json;
using Microsoft.SemanticKernel;
using SemanticKernelAssistant.Web.Data;
using SemanticKernelAssistant.Web.Services;

namespace SemanticKernelAssistant.Web.Plugins;

public sealed class ReminderPlugin(IReminderStore reminderStore, IClock clock)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [KernelFunction]
    [Description("Create a local reminder or alarm. Use this when the user asks to be reminded, set an alarm, or follow up later.")]
    public async Task<string> CreateReminderAsync(
        [Description("Short reminder title, such as 'Team standup' or 'Take medicine'.")]
        string title,
        [Description("When the reminder is due. Prefer ISO 8601 like 2026-09-05T15:00:00, or a relative phrase such as 'in 10 minutes', 'tomorrow 9am'.")]
        string dueAt,
        [Description("Optional extra notes for the reminder.")]
        string? notes = null)
    {
        if (!ReminderDueParser.TryParse(dueAt, clock, out var dueAtUtc, out var error))
        {
            return JsonSerializer.Serialize(new { error }, JsonOptions);
        }

        try
        {
            var reminder = await reminderStore.CreateAsync(title, dueAtUtc, notes, CancellationToken.None);
            return JsonSerializer.Serialize(
                new
                {
                    reminder.Id,
                    reminder.Title,
                    reminder.Notes,
                    dueAtUtc = reminder.DueAtUtc,
                    reminder.Status,
                    message =
                        $"Reminder scheduled for {IndiaTimeZone.ToIndiaTime(reminder.DueAtUtc):yyyy-MM-dd h:mm tt} IST."
                },
                JsonOptions);
        }
        catch (ArgumentException exception)
        {
            return JsonSerializer.Serialize(new { error = exception.Message }, JsonOptions);
        }
    }

    [KernelFunction]
    [Description("List upcoming pending reminders and recently fired reminders.")]
    public async Task<string> ListRemindersAsync()
    {
        var reminders = await reminderStore.ListAsync(CancellationToken.None);
        return JsonSerializer.Serialize(reminders, JsonOptions);
    }

    [KernelFunction]
    [Description("Mark a reminder as completed using its id.")]
    public async Task<string> CompleteReminderAsync(
        [Description("The reminder id returned when the reminder was created.")]
        string reminderId)
    {
        return await UpdateAsync(reminderId, reminderStore.CompleteAsync);
    }

    [KernelFunction]
    [Description("Cancel a pending reminder using its id.")]
    public async Task<string> CancelReminderAsync(
        [Description("The reminder id returned when the reminder was created.")]
        string reminderId)
    {
        return await UpdateAsync(reminderId, reminderStore.CancelAsync);
    }

    private async Task<string> UpdateAsync(
        string reminderId,
        Func<Guid, CancellationToken, Task<Reminder>> update)
    {
        if (!Guid.TryParse(reminderId, out var id))
        {
            return JsonSerializer.Serialize(new { error = "A valid reminder id is required." }, JsonOptions);
        }

        try
        {
            var reminder = await update(id, CancellationToken.None);
            return JsonSerializer.Serialize(
                new
                {
                    reminder.Id,
                    reminder.Title,
                    reminder.Status,
                    dueAtUtc = reminder.DueAtUtc
                },
                JsonOptions);
        }
        catch (KeyNotFoundException exception)
        {
            return JsonSerializer.Serialize(new { error = exception.Message }, JsonOptions);
        }
    }
}

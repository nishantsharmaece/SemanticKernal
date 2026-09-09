namespace SemanticKernelAssistant.Web.Data;

public sealed class Reminder
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = string.Empty;

    public string? Notes { get; set; }

    public DateTime DueAtUtc { get; set; }

    public string Status { get; set; } = ReminderStatuses.Pending;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? FiredAtUtc { get; set; }
}

public static class ReminderStatuses
{
    public const string Pending = "pending";
    public const string Fired = "fired";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
}

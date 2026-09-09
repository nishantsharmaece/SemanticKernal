namespace SemanticKernelAssistant.Web.Models;

public sealed record CreateConversationRequest(string? Title);

public sealed record SendMessageRequest(string? Message);

public sealed record ConversationSummary(
    Guid Id,
    string Title,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record MessageResponse(
    Guid Id,
    Guid ConversationId,
    string Role,
    string Content,
    DateTime CreatedAtUtc);

public sealed record OllamaStatusResponse(
    bool Available,
    string Endpoint,
    string Model,
    string? Message);

public sealed record ReminderResponse(
    Guid Id,
    string Title,
    string? Notes,
    DateTime DueAtUtc,
    string Status,
    DateTime CreatedAtUtc,
    DateTime? FiredAtUtc);

public sealed record DailyBriefingResponse(
    DateTime GeneratedAtUtc,
    string LocalDate,
    int PendingCount,
    int DueTodayCount,
    int OverdueCount,
    IReadOnlyList<ReminderResponse> DueToday,
    IReadOnlyList<ReminderResponse> Overdue);

public sealed record CalendarEventSummary(
    string Id,
    string Subject,
    DateTime StartUtc,
    DateTime EndUtc,
    string? Location);

public sealed record CalendarInviteResult(
    string Id,
    string Subject,
    DateTime StartUtc,
    DateTime EndUtc,
    IReadOnlyList<string> Attendees,
    string? HtmlLink);

public sealed record GoogleStatusResponse(
    bool Configured,
    bool Connected,
    string? Message);

namespace SemanticKernelAssistant.Web.Services;

public sealed class AssistantOptions
{
    public const string SectionName = "Assistant";

    public string Endpoint { get; set; } = "http://localhost:11434";

    public string Model { get; set; } = "llama3.2:3b";

    public int ContextSize { get; set; } = 2_048;

    public int MaximumOutputTokens { get; set; } = 256;

    public int MaximumHistoryMessages { get; set; } = 12;

    public int KeepAliveMinutes { get; set; } = 30;

    public int WarmupIntervalMinutes { get; set; } = 4;

    public string SystemPrompt { get; set; } =
        "You are a concise personal assistant running in this local web app. "
        + "Use Reminders for local alarms only. Those appear in the left sidebar Reminders list. "
        + "Never mention Apple Reminders, Windows Reminders, or a separate Reminder app. "
        + "Use Briefing for daily briefing / agenda / what is due today from local reminders. "
        + "Use Calendar CreateMeetingInvite when the user wants to invite someone, send a Gmail/Google Calendar invitation, or schedule a meeting with attendees. "
        + "Only confirm an invitation after the tool returns success. "
        + "If the tool returns that Google is not connected, tell the user to click Connect Google in the sidebar. "
        + "Never invent that an email or invite was sent.";
}

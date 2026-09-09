namespace SemanticKernelAssistant.Web.Data;

public sealed class Conversation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = "New conversation";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<ConversationMessage> Messages { get; set; } = [];
}

public sealed class ConversationMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ConversationId { get; set; }

    public Conversation Conversation { get; set; } = null!;

    public string Role { get; set; } = MessageRoles.User;

    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public static class MessageRoles
{
    public const string User = "user";
    public const string Assistant = "assistant";
}

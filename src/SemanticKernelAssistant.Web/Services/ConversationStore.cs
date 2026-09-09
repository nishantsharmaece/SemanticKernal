using Microsoft.EntityFrameworkCore;
using SemanticKernelAssistant.Web.Data;
using SemanticKernelAssistant.Web.Models;

namespace SemanticKernelAssistant.Web.Services;

public interface IConversationStore
{
    Task<IReadOnlyList<ConversationSummary>> ListAsync(CancellationToken cancellationToken);

    Task<Conversation> CreateAsync(string? title, CancellationToken cancellationToken);

    Task<Conversation?> GetWithMessagesAsync(Guid id, CancellationToken cancellationToken);

    Task<ConversationMessage> AddMessageAsync(
        Guid conversationId,
        string role,
        string content,
        CancellationToken cancellationToken);
}

public sealed class ConversationStore(AssistantDbContext dbContext) : IConversationStore
{
    public async Task<IReadOnlyList<ConversationSummary>> ListAsync(
        CancellationToken cancellationToken)
    {
        return await dbContext.Conversations
            .AsNoTracking()
            .OrderByDescending(item => item.UpdatedAtUtc)
            .Select(item => new ConversationSummary(
                item.Id,
                item.Title,
                item.CreatedAtUtc,
                item.UpdatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<Conversation> CreateAsync(
        string? title,
        CancellationToken cancellationToken)
    {
        var conversation = new Conversation
        {
            Title = NormalizeTitle(title)
        };

        dbContext.Conversations.Add(conversation);
        await dbContext.SaveChangesAsync(cancellationToken);
        return conversation;
    }

    public Task<Conversation?> GetWithMessagesAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        return dbContext.Conversations
            .AsNoTracking()
            .Include(item => item.Messages.OrderBy(message => message.CreatedAtUtc))
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
    }

    public async Task<ConversationMessage> AddMessageAsync(
        Guid conversationId,
        string role,
        string content,
        CancellationToken cancellationToken)
    {
        var conversation = await dbContext.Conversations
            .SingleOrDefaultAsync(item => item.Id == conversationId, cancellationToken)
            ?? throw new KeyNotFoundException("Conversation was not found.");

        var now = DateTime.UtcNow;
        var message = new ConversationMessage
        {
            ConversationId = conversationId,
            Role = role,
            Content = content,
            CreatedAtUtc = now
        };

        if (conversation.Title == "New conversation" && role == MessageRoles.User)
        {
            conversation.Title = NormalizeTitle(content);
        }

        conversation.UpdatedAtUtc = now;
        dbContext.Messages.Add(message);
        await dbContext.SaveChangesAsync(cancellationToken);
        return message;
    }

    private static string NormalizeTitle(string? title)
    {
        var value = string.IsNullOrWhiteSpace(title) ? "New conversation" : title.Trim();
        return value.Length <= 120 ? value : value[..117] + "...";
    }
}

using System.Text;
using SemanticKernelAssistant.Web.Data;

namespace SemanticKernelAssistant.Web.Services;

public interface IChatAssistant
{
    Task<ConversationMessage> SendAsync(
        Guid conversationId,
        string? message,
        CancellationToken cancellationToken);

    Task<ConversationMessage> SendStreamingAsync(
        Guid conversationId,
        string? message,
        Func<string, CancellationToken, ValueTask> onChunk,
        CancellationToken cancellationToken);
}

public sealed class ChatAssistant(
    IConversationStore conversationStore,
    IAiChatClient aiChatClient) : IChatAssistant
{
    public const int MaximumMessageLength = 4_000;

    public async Task<ConversationMessage> SendAsync(
        Guid conversationId,
        string? message,
        CancellationToken cancellationToken)
    {
        return await SendStreamingAsync(
            conversationId,
            message,
            static (_, _) => ValueTask.CompletedTask,
            cancellationToken);
    }

    public async Task<ConversationMessage> SendStreamingAsync(
        Guid conversationId,
        string? message,
        Func<string, CancellationToken, ValueTask> onChunk,
        CancellationToken cancellationToken)
    {
        var content = message?.Trim();
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("A message is required.", nameof(message));
        }

        if (content.Length > MaximumMessageLength)
        {
            throw new ArgumentException(
                $"Messages cannot exceed {MaximumMessageLength} characters.",
                nameof(message));
        }

        await conversationStore.AddMessageAsync(
            conversationId,
            MessageRoles.User,
            content,
            cancellationToken);

        var conversation = await conversationStore.GetWithMessagesAsync(
            conversationId,
            cancellationToken);

        var responseBuilder = new StringBuilder();
        await foreach (var chunk in aiChatClient.CompleteStreamingAsync(
                           conversation!.Messages.ToList(),
                           cancellationToken))
        {
            responseBuilder.Append(chunk);
            await onChunk(chunk, cancellationToken);
        }

        var response = SemanticKernelChatClient.NormalizeResponse(responseBuilder.ToString());
        if (responseBuilder.Length == 0)
        {
            await onChunk(response, cancellationToken);
        }

        return await conversationStore.AddMessageAsync(
            conversationId,
            MessageRoles.Assistant,
            response,
            cancellationToken);
    }
}

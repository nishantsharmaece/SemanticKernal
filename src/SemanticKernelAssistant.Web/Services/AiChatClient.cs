using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.Ollama;
using SemanticKernelAssistant.Web.Data;

namespace SemanticKernelAssistant.Web.Services;

public interface IAiChatClient
{
    Task<string> CompleteAsync(
        IReadOnlyCollection<ConversationMessage> messages,
        CancellationToken cancellationToken);

    IAsyncEnumerable<string> CompleteStreamingAsync(
        IReadOnlyCollection<ConversationMessage> messages,
        CancellationToken cancellationToken);
}

public sealed class SemanticKernelChatClient(
    Kernel kernel,
    IChatCompletionService chatCompletionService,
    IOptions<AssistantOptions> options,
    ILogger<SemanticKernelChatClient> logger) : IAiChatClient
{
    public async Task<string> CompleteAsync(
        IReadOnlyCollection<ConversationMessage> messages,
        CancellationToken cancellationToken)
    {
        var response = new StringBuilder();
        await foreach (var chunk in CompleteStreamingAsync(messages, cancellationToken))
        {
            response.Append(chunk);
        }

        return NormalizeResponse(response.ToString());
    }

    public async IAsyncEnumerable<string> CompleteStreamingAsync(
        IReadOnlyCollection<ConversationMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var history = new ChatHistory();
        history.AddSystemMessage(options.Value.SystemPrompt);

        foreach (var message in SelectRecentMessages(
                     messages,
                     options.Value.MaximumHistoryMessages))
        {
            if (message.Role == MessageRoles.User)
            {
                history.AddUserMessage(message.Content);
            }
            else if (message.Role == MessageRoles.Assistant)
            {
                history.AddAssistantMessage(message.Content);
            }
        }

        var enableTools = ShouldEnableReminderFunctions(messages)
            || ShouldEnableBriefingFunctions(messages)
            || ShouldEnableCalendarFunctions(messages);
        var settings = new OllamaPromptExecutionSettings
        {
            FunctionChoiceBehavior = enableTools
                ? FunctionChoiceBehavior.Auto()
                : null,
            NumPredict = options.Value.MaximumOutputTokens,
            ExtensionData = new Dictionary<string, object>
            {
                ["num_ctx"] = options.Value.ContextSize
            }
        };

        var updates = chatCompletionService
            .GetStreamingChatMessageContentsAsync(
                history,
                settings,
                enableTools ? kernel : null,
                cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        try
        {
            while (true)
            {
                string? content;
                try
                {
                    if (!await updates.MoveNextAsync())
                    {
                        break;
                    }

                    content = updates.Current.Content;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Ollama chat completion failed.");
                    throw new AiServiceUnavailableException(
                        "Ollama is unavailable. Start Ollama and pull the configured model.",
                        exception);
                }

                if (!string.IsNullOrEmpty(content))
                {
                    yield return content;
                }
            }
        }
        finally
        {
            await updates.DisposeAsync();
        }
    }

    public static IReadOnlyList<ConversationMessage> SelectRecentMessages(
        IEnumerable<ConversationMessage> messages,
        int maximumMessages)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumMessages, 1);

        return messages
            .OrderBy(item => item.CreatedAtUtc)
            .TakeLast(maximumMessages)
            .ToList();
    }

    public static bool ShouldEnableReminderFunctions(
        IEnumerable<ConversationMessage> messages)
    {
        var latestUserMessage = messages
            .Where(item => item.Role == MessageRoles.User)
            .OrderByDescending(item => item.CreatedAtUtc)
            .FirstOrDefault()
            ?.Content;

        if (string.IsNullOrWhiteSpace(latestUserMessage))
        {
            return false;
        }

        return latestUserMessage.Contains("remind", StringComparison.OrdinalIgnoreCase)
            || latestUserMessage.Contains("alarm", StringComparison.OrdinalIgnoreCase);
    }

    public static bool ShouldEnableBriefingFunctions(
        IEnumerable<ConversationMessage> messages)
    {
        var latestUserMessage = messages
            .Where(item => item.Role == MessageRoles.User)
            .OrderByDescending(item => item.CreatedAtUtc)
            .FirstOrDefault()
            ?.Content;

        if (string.IsNullOrWhiteSpace(latestUserMessage))
        {
            return false;
        }

        return latestUserMessage.Contains("briefing", StringComparison.OrdinalIgnoreCase)
            || latestUserMessage.Contains("agenda", StringComparison.OrdinalIgnoreCase)
            || latestUserMessage.Contains("what do i have today", StringComparison.OrdinalIgnoreCase)
            || latestUserMessage.Contains("what's on today", StringComparison.OrdinalIgnoreCase);
    }

    public static bool ShouldEnableCalendarFunctions(
        IEnumerable<ConversationMessage> messages)
    {
        var latestUserMessage = messages
            .Where(item => item.Role == MessageRoles.User)
            .OrderByDescending(item => item.CreatedAtUtc)
            .FirstOrDefault()
            ?.Content;

        if (string.IsNullOrWhiteSpace(latestUserMessage))
        {
            return false;
        }

        return latestUserMessage.Contains("invite", StringComparison.OrdinalIgnoreCase)
            || latestUserMessage.Contains("invitation", StringComparison.OrdinalIgnoreCase)
            || latestUserMessage.Contains("gmail", StringComparison.OrdinalIgnoreCase)
            || latestUserMessage.Contains("calendar", StringComparison.OrdinalIgnoreCase)
            || latestUserMessage.Contains("meeting", StringComparison.OrdinalIgnoreCase)
            || latestUserMessage.Contains("schedule", StringComparison.OrdinalIgnoreCase)
            || latestUserMessage.Contains("attendee", StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeResponse(string response)
    {
        return string.IsNullOrWhiteSpace(response)
            ? "I updated your reminders, but the model returned no text. Check the Reminders list."
            : response.Trim();
    }
}

public sealed class AiServiceUnavailableException : Exception
{
    public AiServiceUnavailableException(string message)
        : base(message)
    {
    }

    public AiServiceUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

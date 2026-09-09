using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SemanticKernelAssistant.Web.Data;
using SemanticKernelAssistant.Web.Services;

namespace SemanticKernelAssistant.Tests.Services;

public sealed class AiPerformanceTests
{
    [Fact]
    public void SelectRecentMessagesKeepsLatestMessagesInChronologicalOrder()
    {
        var messages = Enumerable.Range(1, 20)
            .Select(index => new ConversationMessage
            {
                Content = $"Message {index}",
                CreatedAtUtc = new DateTime(2026, 1, 1).AddMinutes(index)
            })
            .Reverse()
            .ToList();

        var selected = SemanticKernelChatClient.SelectRecentMessages(messages, 12);

        Assert.Equal(12, selected.Count);
        Assert.Equal("Message 9", selected[0].Content);
        Assert.Equal("Message 20", selected[^1].Content);
    }

    [Theory]
    [InlineData("Explain dependency injection", false)]
    [InlineData("Remind me in ten minutes", true)]
    [InlineData("Set an alarm for 9am", true)]
    [InlineData("What reminders do I have?", true)]
    public void ReminderFunctionsAreOnlyEnabledForReminderRequests(
        string content,
        bool expected)
    {
        var messages = new[]
        {
            new ConversationMessage
            {
                Role = MessageRoles.User,
                Content = content,
                CreatedAtUtc = DateTime.UtcNow
            }
        };

        Assert.Equal(
            expected,
            SemanticKernelChatClient.ShouldEnableReminderFunctions(messages));
    }

    [Theory]
    [InlineData("Explain dependency injection", false)]
    [InlineData("Give me my daily briefing", true)]
    [InlineData("What's my agenda", true)]
    [InlineData("What do I have today?", true)]
    public void BriefingFunctionsAreOnlyEnabledForBriefingRequests(
        string content,
        bool expected)
    {
        var messages = new[]
        {
            new ConversationMessage
            {
                Role = MessageRoles.User,
                Content = content,
                CreatedAtUtc = DateTime.UtcNow
            }
        };

        Assert.Equal(
            expected,
            SemanticKernelChatClient.ShouldEnableBriefingFunctions(messages));
    }

    [Theory]
    [InlineData("Explain dependency injection", false)]
    [InlineData("Invite bob@gmail.com tomorrow 10am", true)]
    [InlineData("Send a calendar invitation", true)]
    [InlineData("Schedule a meeting with an attendee", true)]
    public void CalendarFunctionsAreOnlyEnabledForCalendarRequests(
        string content,
        bool expected)
    {
        var messages = new[]
        {
            new ConversationMessage
            {
                Role = MessageRoles.User,
                Content = content,
                CreatedAtUtc = DateTime.UtcNow
            }
        };

        Assert.Equal(
            expected,
            SemanticKernelChatClient.ShouldEnableCalendarFunctions(messages));
    }

    [Fact]
    public async Task ModelWarmerSendsConfiguredKeepAlive()
    {
        string? requestBody = null;
        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var warmer = CreateWarmer(handler);

        var warmed = await warmer.WarmAsync(CancellationToken.None);

        Assert.True(warmed);
        Assert.Contains("\"model\":\"llama3.2:3b\"", requestBody);
        Assert.Contains("\"keep_alive\":\"30m\"", requestBody);
    }

    [Fact]
    public async Task ModelWarmerReturnsFalseWhenOllamaIsUnavailable()
    {
        var handler = new StubHttpMessageHandler(
            (_, _) => throw new HttpRequestException("Ollama unavailable"));
        var warmer = CreateWarmer(handler);

        var warmed = await warmer.WarmAsync(CancellationToken.None);

        Assert.False(warmed);
    }

    private static OllamaModelWarmer CreateWarmer(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:11434")
        };

        return new OllamaModelWarmer(
            client,
            Options.Create(new AssistantOptions()),
            NullLogger<OllamaModelWarmer>.Instance);
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return send(request, cancellationToken);
        }
    }
}

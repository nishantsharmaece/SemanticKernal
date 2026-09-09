using System.Runtime.CompilerServices;
using System.Text;
using SemanticKernelAssistant.Tests.Data;
using SemanticKernelAssistant.Web.Data;
using SemanticKernelAssistant.Web.Services;

namespace SemanticKernelAssistant.Tests.Services;

public sealed class ChatAssistantTests
{
    [Fact]
    public async Task SendPersistsUserAndAssistantMessages()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = new ConversationStore(database.Context);
        var conversation = await store.CreateAsync(null, CancellationToken.None);
        var aiClient = new FakeAiChatClient("Local response");
        var assistant = new ChatAssistant(store, aiClient);

        var response = await assistant.SendAsync(
            conversation.Id,
            "Hello assistant",
            CancellationToken.None);
        var saved = await store.GetWithMessagesAsync(conversation.Id, CancellationToken.None);

        Assert.Equal(MessageRoles.Assistant, response.Role);
        Assert.Equal("Local response", response.Content);
        Assert.NotNull(saved);
        Assert.Collection(
            saved.Messages,
            message =>
            {
                Assert.Equal(MessageRoles.User, message.Role);
                Assert.Equal("Hello assistant", message.Content);
            },
            message =>
            {
                Assert.Equal(MessageRoles.Assistant, message.Role);
                Assert.Equal("Local response", message.Content);
            });
        Assert.Contains(
            aiClient.ReceivedMessages,
            message => message.Role == MessageRoles.User && message.Content == "Hello assistant");
    }

    [Fact]
    public async Task SendStreamingForwardsChunksAndPersistsOneFinalMessage()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = new ConversationStore(database.Context);
        var conversation = await store.CreateAsync(null, CancellationToken.None);
        var assistant = new ChatAssistant(store, new FakeAiChatClient("Local response"));
        var streamed = new StringBuilder();

        var response = await assistant.SendStreamingAsync(
            conversation.Id,
            "Hello",
            (chunk, _) =>
            {
                streamed.Append(chunk);
                return ValueTask.CompletedTask;
            },
            CancellationToken.None);

        var saved = await store.GetWithMessagesAsync(conversation.Id, CancellationToken.None);
        Assert.Equal("Local response", streamed.ToString());
        Assert.Equal("Local response", response.Content);
        Assert.Equal(2, saved!.Messages.Count);
        Assert.Single(saved.Messages, message => message.Role == MessageRoles.Assistant);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SendRejectsEmptyMessages(string? message)
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = new ConversationStore(database.Context);
        var conversation = await store.CreateAsync(null, CancellationToken.None);
        var assistant = new ChatAssistant(store, new FakeAiChatClient("Unused"));

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => assistant.SendAsync(conversation.Id, message, CancellationToken.None));

        Assert.Contains("required", exception.Message);
        var saved = await store.GetWithMessagesAsync(conversation.Id, CancellationToken.None);
        Assert.Empty(saved!.Messages);
    }

    [Fact]
    public async Task SendRejectsOversizedMessages()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = new ConversationStore(database.Context);
        var conversation = await store.CreateAsync(null, CancellationToken.None);
        var assistant = new ChatAssistant(store, new FakeAiChatClient("Unused"));

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => assistant.SendAsync(
                conversation.Id,
                new string('x', ChatAssistant.MaximumMessageLength + 1),
                CancellationToken.None));

        Assert.Contains("cannot exceed", exception.Message);
    }

    private sealed class FakeAiChatClient(string response) : IAiChatClient
    {
        public IReadOnlyCollection<ConversationMessage> ReceivedMessages { get; private set; } = [];

        public Task<string> CompleteAsync(
            IReadOnlyCollection<ConversationMessage> messages,
            CancellationToken cancellationToken)
        {
            ReceivedMessages = messages;
            return Task.FromResult(response);
        }

        public async IAsyncEnumerable<string> CompleteStreamingAsync(
            IReadOnlyCollection<ConversationMessage> messages,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            ReceivedMessages = messages;
            var midpoint = response.Length / 2;
            yield return response[..midpoint];
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            yield return response[midpoint..];
        }
    }
}

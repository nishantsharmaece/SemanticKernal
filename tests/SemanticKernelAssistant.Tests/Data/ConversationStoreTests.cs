using Microsoft.EntityFrameworkCore;
using SemanticKernelAssistant.Web.Data;
using SemanticKernelAssistant.Web.Services;

namespace SemanticKernelAssistant.Tests.Data;

public sealed class ConversationStoreTests
{
    [Fact]
    public async Task MigrationCreatesDatabaseAndTables()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"semantic-kernel-assistant-{Guid.NewGuid():N}.db");

        try
        {
            var options = new DbContextOptionsBuilder<AssistantDbContext>()
                .UseSqlite($"Data Source={databasePath};Pooling=False")
                .Options;

            await using (var context = new AssistantDbContext(options))
            {
                await context.Database.MigrateAsync();

                Assert.True(File.Exists(databasePath));
                Assert.True(await context.Conversations.AnyAsync() is false);
            }
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task StorePersistsMessagesAndMovesUpdatedConversationFirst()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = new ConversationStore(database.Context);
        var first = await store.CreateAsync("First", CancellationToken.None);
        var second = await store.CreateAsync("Second", CancellationToken.None);

        await store.AddMessageAsync(
            first.Id,
            MessageRoles.User,
            "Hello",
            CancellationToken.None);

        var conversations = await store.ListAsync(CancellationToken.None);
        var saved = await store.GetWithMessagesAsync(
            first.Id,
            CancellationToken.None);

        Assert.Equal(first.Id, conversations[0].Id);
        Assert.Equal(second.Id, conversations[1].Id);
        Assert.NotNull(saved);
        Assert.Collection(
            saved.Messages,
            message =>
            {
                Assert.Equal(MessageRoles.User, message.Role);
                Assert.Equal("Hello", message.Content);
            });
    }

    [Fact]
    public async Task FirstUserMessageBecomesConversationTitle()
    {
        await using var database = await TestDatabase.CreateAsync();
        var store = new ConversationStore(database.Context);
        var conversation = await store.CreateAsync(null, CancellationToken.None);

        await store.AddMessageAsync(
            conversation.Id,
            MessageRoles.User,
            "Summarize this project",
            CancellationToken.None);

        var conversations = await store.ListAsync(CancellationToken.None);
        Assert.Equal("Summarize this project", conversations[0].Title);
    }
}

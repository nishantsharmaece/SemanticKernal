using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SemanticKernelAssistant.Web.Data;

namespace SemanticKernelAssistant.Tests.Data;

internal sealed class TestDatabase : IAsyncDisposable
{
    private readonly SqliteConnection connection;

    private TestDatabase(SqliteConnection connection, AssistantDbContext context)
    {
        this.connection = connection;
        Context = context;
    }

    public AssistantDbContext Context { get; }

    public static async Task<TestDatabase> CreateAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AssistantDbContext>()
            .UseSqlite(connection)
            .Options;
        var context = new AssistantDbContext(options);
        await context.Database.EnsureCreatedAsync();

        return new TestDatabase(connection, context);
    }

    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();
        await connection.DisposeAsync();
    }
}

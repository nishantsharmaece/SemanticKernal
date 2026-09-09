using Microsoft.EntityFrameworkCore;

namespace SemanticKernelAssistant.Web.Data;

public sealed class AssistantDbContext(DbContextOptions<AssistantDbContext> options)
    : DbContext(options)
{
    public DbSet<Conversation> Conversations => Set<Conversation>();

    public DbSet<ConversationMessage> Messages => Set<ConversationMessage>();

    public DbSet<Reminder> Reminders => Set<Reminder>();

    public DbSet<OAuthToken> OAuthTokens => Set<OAuthToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var conversation = modelBuilder.Entity<Conversation>();
        conversation.HasKey(item => item.Id);
        conversation.Property(item => item.Title).HasMaxLength(120).IsRequired();
        conversation.HasIndex(item => item.UpdatedAtUtc);

        var message = modelBuilder.Entity<ConversationMessage>();
        message.HasKey(item => item.Id);
        message.Property(item => item.Role).HasMaxLength(20).IsRequired();
        message.Property(item => item.Content).HasMaxLength(16_000).IsRequired();
        message.HasIndex(item => new { item.ConversationId, item.CreatedAtUtc });
        message
            .HasOne(item => item.Conversation)
            .WithMany(item => item.Messages)
            .HasForeignKey(item => item.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        var reminder = modelBuilder.Entity<Reminder>();
        reminder.HasKey(item => item.Id);
        reminder.Property(item => item.Title).HasMaxLength(200).IsRequired();
        reminder.Property(item => item.Notes).HasMaxLength(2_000);
        reminder.Property(item => item.Status).HasMaxLength(20).IsRequired();
        reminder.HasIndex(item => new { item.Status, item.DueAtUtc });

        var token = modelBuilder.Entity<OAuthToken>();
        token.HasKey(item => item.Id);
        token.Property(item => item.Provider).HasMaxLength(40).IsRequired();
        token.Property(item => item.AccessToken).IsRequired();
        token.HasIndex(item => item.Provider).IsUnique();
    }
}

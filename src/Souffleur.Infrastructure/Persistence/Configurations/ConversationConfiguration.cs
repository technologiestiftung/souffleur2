using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souffleur.Domain.Conversations;

namespace Souffleur.Infrastructure.Persistence.Configurations;

public class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.Property(c => c.Name).HasMaxLength(120);
        builder.Property(c => c.Topic).HasMaxLength(500);

        // Je Gegenstand höchstens eine Unterhaltung -- sonst entstehen zwei
        // Fäden an derselben Rechnung und keiner weiß, in welchem geantwortet wird.
        builder.HasIndex(c => new { c.SubjectType, c.SubjectId })
               .IsUnique()
               .HasFilter("SubjectId IS NOT NULL");

        builder.HasIndex(c => c.LastMessageAt);

        builder.HasMany(c => c.Participants)
               .WithOne(p => p.Conversation)
               .HasForeignKey(p => p.ConversationId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.Messages)
               .WithOne(m => m.Conversation)
               .HasForeignKey(m => m.ConversationId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

public class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.Property(m => m.Text).HasMaxLength(8000);

        builder.HasIndex(m => new { m.ConversationId, m.CreatedAt });

        builder.HasOne(m => m.Author).WithMany()
               .HasForeignKey(m => m.AuthorId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.InReplyTo).WithMany()
               .HasForeignKey(m => m.InReplyToId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(m => m.Mentions)
               .WithOne(x => x.Message)
               .HasForeignKey(x => x.MessageId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

public class MessageMentionConfiguration : IEntityTypeConfiguration<MessageMention>
{
    public void Configure(EntityTypeBuilder<MessageMention> builder)
    {
        builder.HasIndex(m => new { m.UserId, m.ReadAt });

        builder.HasOne(m => m.User).WithMany()
               .HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ConversationParticipantConfiguration : IEntityTypeConfiguration<ConversationParticipant>
{
    public void Configure(EntityTypeBuilder<ConversationParticipant> builder)
    {
        // Jede Person höchstens einmal je Unterhaltung.
        builder.HasIndex(p => new { p.ConversationId, p.UserId }).IsUnique();

        builder.HasOne(p => p.User).WithMany()
               .HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

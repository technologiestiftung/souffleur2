using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souffleur.Domain.Mail;

namespace Souffleur.Infrastructure.Persistence.Configurations;

public class EmailMessageConfiguration : IEntityTypeConfiguration<EmailMessage>
{
    public void Configure(EntityTypeBuilder<EmailMessage> builder)
    {
        builder.Property(m => m.FromAddress).HasMaxLength(320);
        builder.Property(m => m.FromName).HasMaxLength(200);
        builder.Property(m => m.Subject).HasMaxLength(EmailMessage.MaxSubjectLength);
        builder.Property(m => m.Body).HasMaxLength(EmailMessage.MaxBodyLength);
        builder.Property(m => m.MessageId).HasMaxLength(998);
        builder.Ignore(m => m.IsUnread);

        // Die Leiste fragt „neueste zuerst"; das Abholen „welche kenne ich schon".
        builder.HasIndex(m => m.SentAt);
        builder.HasIndex(m => m.MessageId).IsUnique().HasFilter("MessageId IS NOT NULL");
        builder.HasIndex(m => new { m.Direction, m.ReadAt });

        builder.HasOne(m => m.SenderUser).WithMany()
               .HasForeignKey(m => m.SenderUserId).OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(m => m.Contact).WithMany()
               .HasForeignKey(m => m.ContactId).OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(m => m.Recipients)
               .WithOne(r => r.EmailMessage)
               .HasForeignKey(r => r.EmailMessageId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

public class EmailRecipientConfiguration : IEntityTypeConfiguration<EmailRecipient>
{
    public void Configure(EntityTypeBuilder<EmailRecipient> builder)
    {
        builder.Property(r => r.Address).HasMaxLength(320);
        builder.Property(r => r.Name).HasMaxLength(200);
        builder.Ignore(r => r.Display);

        builder.HasIndex(r => r.ContactId);

        builder.HasOne(r => r.Contact).WithMany()
               .HasForeignKey(r => r.ContactId).OnDelete(DeleteBehavior.SetNull);
    }
}

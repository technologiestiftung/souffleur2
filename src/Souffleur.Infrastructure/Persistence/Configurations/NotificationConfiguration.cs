using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souffleur.Domain.Identity;
using Souffleur.Domain.Notifications;

namespace Souffleur.Infrastructure.Persistence.Configurations;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.Property(n => n.Title).HasMaxLength(300);
        builder.Property(n => n.Body).HasMaxLength(1000);
        builder.Property(n => n.Link).HasMaxLength(300);
        builder.Ignore(n => n.IsRead);

        // Die Glocke fragt „meine, neueste zuerst"; der Versand „noch nicht gemailt".
        builder.HasIndex(n => new { n.UserId, n.CreatedAt });
        builder.HasIndex(n => n.EmailedAt);

        builder.HasOne<User>()
               .WithMany()
               .HasForeignKey(n => n.UserId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souffleur.Domain.Bookmarks;

namespace Souffleur.Infrastructure.Persistence.Configurations;

public class BookmarkConfiguration : IEntityTypeConfiguration<Bookmark>
{
    public void Configure(EntityTypeBuilder<Bookmark> builder)
    {
        builder.Property(b => b.Url).HasMaxLength(2000);
        builder.Property(b => b.Label).HasMaxLength(200);

        // Die eine Frage, die gestellt wird: Was haengt an diesem Gegenstand?
        builder.HasIndex(b => new { b.Target, b.TargetId });
    }
}

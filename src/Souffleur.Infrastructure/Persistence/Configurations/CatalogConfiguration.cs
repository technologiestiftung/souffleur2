using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souffleur.Domain.Catalogs;

namespace Souffleur.Infrastructure.Persistence.Configurations;

public class CatalogEntryConfiguration : IEntityTypeConfiguration<CatalogEntry>
{
    public void Configure(EntityTypeBuilder<CatalogEntry> builder)
    {
        builder.Property(e => e.Key).HasMaxLength(60);
        builder.Property(e => e.Label).HasMaxLength(120);
        builder.Property(e => e.Code).HasMaxLength(20);
        builder.Property(e => e.Description).HasMaxLength(1000);
        builder.Property(e => e.Color).HasMaxLength(9);

        // Der Schlüssel ist innerhalb seiner Liste eindeutig, nicht darüber
        // hinaus: „probe" darf es bei den Terminen und bei den Aufgaben geben.
        builder.HasIndex(e => new { e.Catalog, e.Key }).IsUnique();
        builder.HasIndex(e => new { e.Catalog, e.SortOrder });

        // Ein übergeordneter Eintrag lässt sich nicht löschen, solange etwas
        // unter ihm hängt -- sonst verlören die Kinder ihre Einordnung.
        builder.HasOne(e => e.Parent)
               .WithMany()
               .HasForeignKey(e => e.ParentId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souffleur.Domain.Calendar;
using Souffleur.Domain.Catalogs;
using Souffleur.Domain.Resources;

namespace Souffleur.Infrastructure.Persistence.Configurations;

public class ResourceConfiguration : IEntityTypeConfiguration<Resource>
{
    public void Configure(EntityTypeBuilder<Resource> builder)
    {
        builder.Property(r => r.Name).HasMaxLength(120);
        builder.Property(r => r.ShortName).HasMaxLength(20);
        builder.Property(r => r.Description).HasMaxLength(1000);

        // Zwei Bühnen desselben Namens wären im Spielplan nicht auseinander
        // zu halten -- und die Belegungsprüfung meldete Konflikte, die niemand
        // versteht.
        builder.HasIndex(r => r.Name).IsUnique();
        builder.HasIndex(r => r.SortOrder);

        builder.HasOne(r => r.Kind)
               .WithMany()
               .HasForeignKey(r => r.KindId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ResourceBookingConfiguration : IEntityTypeConfiguration<ResourceBooking>
{
    public void Configure(EntityTypeBuilder<ResourceBooking> builder)
    {
        // Die Prüfung fragt immer von der Ressource aus: Was belegt diesen Saal?
        builder.HasIndex(b => b.ResourceId);
        builder.HasIndex(b => new { b.EventId, b.ResourceId }).IsUnique();
        builder.HasIndex(b => new { b.SeriesId, b.ResourceId }).IsUnique();

        // Die Ressource bleibt, solange sie belegt ist -- abschalten statt
        // löschen, wie bei den Listen des Hauses (ADR-0005).
        builder.HasOne(b => b.Resource)
               .WithMany(r => r.Bookings)
               .HasForeignKey(b => b.ResourceId)
               .OnDelete(DeleteBehavior.Restrict);

        // Die Belegung dagegen hat ohne ihren Termin keinen Sinn.
        builder.HasOne(b => b.Event)
               .WithMany(e => e.Bookings)
               .HasForeignKey(b => b.EventId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(b => b.Series)
               .WithMany(s => s.Bookings)
               .HasForeignKey(b => b.SeriesId)
               .OnDelete(DeleteBehavior.Cascade);

        // Entweder an einem Termin oder an einer Serie, nie an beidem und nie
        // an keinem von beiden.
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_ResourceBooking_EventOrSeries",
            "(EventId IS NULL) <> (SeriesId IS NULL)"));
    }
}

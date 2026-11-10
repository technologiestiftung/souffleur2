using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souffleur.Domain.Touring;

namespace Souffleur.Infrastructure.Persistence.Configurations;

public class TourConfiguration : IEntityTypeConfiguration<Tour>
{
    public void Configure(EntityTypeBuilder<Tour> builder)
    {
        builder.Property(t => t.Title).HasMaxLength(200);
        builder.Property(t => t.Notes).HasMaxLength(4000);

        // Die Produktion bleibt, solange eine Tournee sie traegt -- sie wird
        // abgespielt, nicht geloescht (ADR-0011).
        builder.HasOne(t => t.Production)
               .WithMany()
               .HasForeignKey(t => t.ProductionId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}

public class TourMemberConfiguration : IEntityTypeConfiguration<TourMember>
{
    public void Configure(EntityTypeBuilder<TourMember> builder)
    {
        builder.Property(m => m.Note).HasMaxLength(500);

        // Eine Person reist je Tournee einmal mit.
        builder.HasIndex(m => new { m.TourId, m.ContactId }).IsUnique();
        builder.HasIndex(m => m.ContactId);

        builder.HasOne(m => m.Tour)
               .WithMany(t => t.Members)
               .HasForeignKey(m => m.TourId)
               .OnDelete(DeleteBehavior.Cascade);

        // Der Kontakt bleibt, solange er irgendwo mitreist -- wie bei der
        // Beteiligung am Termin.
        builder.HasOne(m => m.Contact)
               .WithMany()
               .HasForeignKey(m => m.ContactId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.Function)
               .WithMany()
               .HasForeignKey(m => m.FunctionId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}

public class TourStopConfiguration : IEntityTypeConfiguration<TourStop>
{
    public void Configure(EntityTypeBuilder<TourStop> builder)
    {
        builder.Property(s => s.Notes).HasMaxLength(4000);
        builder.Property(s => s.Fee).HasConversion<NullableMoneyConverter>();

        builder.HasIndex(s => s.VenueId);
        builder.HasIndex(s => s.ArrivalDate);

        builder.HasOne(s => s.Tour)
               .WithMany(t => t.Stops)
               .HasForeignKey(s => s.TourId)
               .OnDelete(DeleteBehavior.Cascade);

        // Die Spielstaette von 2024 soll 2027 noch aufloesbar sein.
        builder.HasOne(s => s.Venue)
               .WithMany()
               .HasForeignKey(s => s.VenueId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.ContactPerson)
               .WithMany()
               .HasForeignKey(s => s.ContactPersonId)
               .OnDelete(DeleteBehavior.Restrict);

        // Der Beleg ist aufbewahrungspflichtig und wird ohnehin nie geloescht;
        // faellt die Verknuepfung weg, bleibt der Beleg und das Gastspiel
        // steht wieder als „noch nicht in Rechnung gestellt" da.
        builder.HasOne(s => s.Voucher)
               .WithMany()
               .HasForeignKey(s => s.VoucherId)
               .OnDelete(DeleteBehavior.SetNull);
    }
}

public class TourPerformanceConfiguration : IEntityTypeConfiguration<TourPerformance>
{
    public void Configure(EntityTypeBuilder<TourPerformance> builder)
    {
        builder.Property(p => p.Note).HasMaxLength(500);

        // Der Kalender fragt nach dem Fenster.
        builder.HasIndex(p => p.StartsAt);

        builder.HasOne(p => p.Stop)
               .WithMany(s => s.Performances)
               .HasForeignKey(p => p.StopId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souffleur.Domain.Calendar;
using Souffleur.Domain.Catalogs;
using Souffleur.Domain.Contacts;
using Souffleur.Domain.Productions;

namespace Souffleur.Infrastructure.Persistence.Configurations;

public class CalendarEventConfiguration : IEntityTypeConfiguration<CalendarEvent>
{
    public void Configure(EntityTypeBuilder<CalendarEvent> builder)
    {
        builder.Property(e => e.Title).HasMaxLength(200);
        builder.Property(e => e.Description).HasMaxLength(4000);
        builder.Property(e => e.Location).HasMaxLength(200);

        // Die Zeitraumsuche fragt immer nach einem Fenster: alles, was vor dem
        // Ende beginnt und nach dem Anfang endet.
        builder.HasIndex(e => e.StartsAt);
        builder.HasIndex(e => new { e.SeriesId, e.OccurrenceStart });

        builder.HasOne(e => e.Series)
               .WithMany(s => s.Exceptions)
               .HasForeignKey(e => e.SeriesId)
               .OnDelete(DeleteBehavior.Cascade);

        // Die Kategorie bleibt, solange Termine sie tragen -- deshalb gibt es
        // nur Abschalten, nicht Löschen (ADR-0005).
        builder.HasOne<CatalogEntry>()
               .WithMany()
               .HasForeignKey(e => e.CategoryId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.ProductionId);
        builder.HasOne<Production>()
               .WithMany()
               .HasForeignKey(e => e.ProductionId)
               .OnDelete(DeleteBehavior.SetNull);
    }
}

public class EventSeriesConfiguration : IEntityTypeConfiguration<EventSeries>
{
    public void Configure(EntityTypeBuilder<EventSeries> builder)
    {
        builder.Property(s => s.Title).HasMaxLength(200);
        builder.Property(s => s.Description).HasMaxLength(4000);
        builder.Property(s => s.Location).HasMaxLength(200);
        builder.Property(s => s.TimeZoneId).HasMaxLength(60);

        builder.HasIndex(s => s.StartDate);

        builder.HasOne<CatalogEntry>()
               .WithMany()
               .HasForeignKey(s => s.CategoryId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(s => s.ProductionId);
        builder.HasOne<Production>()
               .WithMany()
               .HasForeignKey(s => s.ProductionId)
               .OnDelete(DeleteBehavior.SetNull);
    }
}

public class CalendarSubscriptionConfiguration : IEntityTypeConfiguration<CalendarSubscription>
{
    public void Configure(EntityTypeBuilder<CalendarSubscription> builder)
    {
        builder.Property(s => s.Name).HasMaxLength(120);
        builder.Property(s => s.Token).HasMaxLength(64);

        // Der Feed findet das Abonnement über das Token und über nichts sonst.
        builder.HasIndex(s => s.Token).IsUnique();

        // Die Filter als Liste in einer Spalte: Sie werden nie einzeln
        // abgefragt, nur zusammen gelesen. Zwei Verknüpfungstabellen wären
        // hier Aufwand ohne Gegenwert.
        builder.PrimitiveCollection(s => s.CategoryIds);
        builder.PrimitiveCollection(s => s.ResourceIds);
        builder.PrimitiveCollection(s => s.ContactIds);
    }
}

public class EventParticipantConfiguration : IEntityTypeConfiguration<EventParticipant>
{
    public void Configure(EntityTypeBuilder<EventParticipant> builder)
    {
        builder.Property(p => p.Note).HasMaxLength(500);

        // Gefragt wird von beiden Seiten: Wer ist an diesem Termin dabei, und
        // wo ist diese Person überall eingeteilt?
        builder.HasIndex(p => p.ContactId);
        builder.HasIndex(p => new { p.EventId, p.ContactId }).IsUnique();
        builder.HasIndex(p => new { p.SeriesId, p.ContactId }).IsUnique();

        // Der Kontakt bleibt, solange er irgendwo eingeteilt ist -- sonst
        // stünde im Probenplan von 2023 ein Name, den niemand mehr auflösen
        // kann.
        builder.HasOne(p => p.Contact)
               .WithMany()
               .HasForeignKey(p => p.ContactId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Function)
               .WithMany()
               .HasForeignKey(p => p.FunctionId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Event)
               .WithMany(e => e.Participants)
               .HasForeignKey(p => p.EventId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(p => p.Series)
               .WithMany(s => s.Participants)
               .HasForeignKey(p => p.SeriesId)
               .OnDelete(DeleteBehavior.Cascade);

        // Entweder an einem Termin oder an einer Serie, nie an beidem.
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_EventParticipant_EventOrSeries",
            "(EventId IS NULL) <> (SeriesId IS NULL)"));
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souffleur.Domain.Personnel;

namespace Souffleur.Infrastructure.Persistence.Configurations;

public class TimeEntryConfiguration : IEntityTypeConfiguration<TimeEntry>
{
    public void Configure(EntityTypeBuilder<TimeEntry> builder)
    {
        builder.Property(e => e.Note).HasMaxLength(500);
        builder.Property(e => e.CorrectionReason).HasMaxLength(500);

        // Die beiden Fragen des Moduls: Was hat diese Person gearbeitet, und
        // laeuft gerade etwas?
        builder.HasIndex(e => new { e.ContactId, e.StartedAt });

        builder.HasOne(e => e.Contact)
               .WithMany()
               .HasForeignKey(e => e.ContactId)
               .OnDelete(DeleteBehavior.Restrict);

        // Funktion und Termin bleiben, solange Zeiten sie tragen -- abschalten
        // statt loeschen (ADR-0005).
        builder.HasOne(e => e.Function)
               .WithMany()
               .HasForeignKey(e => e.FunctionId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Event)
               .WithMany()
               .HasForeignKey(e => e.EventId)
               .OnDelete(DeleteBehavior.SetNull);
    }
}

public class ArtistsSocialFundRateConfiguration : IEntityTypeConfiguration<ArtistsSocialFundRate>
{
    public void Configure(EntityTypeBuilder<ArtistsSocialFundRate> builder)
    {
        // Ein Satz je Jahr.
        builder.HasIndex(r => r.Year).IsUnique();

        // Der Satz hat eine Nachkommastelle, gespeichert wird mit Reserve.
        builder.Property(r => r.Percent).HasPrecision(5, 2);
    }
}

public class DutyTemplateConfiguration : IEntityTypeConfiguration<DutyTemplate>
{
    public void Configure(EntityTypeBuilder<DutyTemplate> builder)
    {
        builder.Property(t => t.Note).HasMaxLength(500);

        // Je Terminart genau ein Bedarf.
        builder.HasIndex(t => t.CategoryId).IsUnique();

        builder.HasOne(t => t.Category)
               .WithMany()
               .HasForeignKey(t => t.CategoryId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

public class DutyRequirementConfiguration : IEntityTypeConfiguration<DutyRequirement>
{
    public void Configure(EntityTypeBuilder<DutyRequirement> builder)
    {
        // Geht die Gruppe, bleibt der Bedarf -- nur ohne Vorschlag.
        builder.HasOne(r => r.Group)
               .WithMany()
               .HasForeignKey(r => r.GroupId)
               .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(r => new { r.TemplateId, r.FunctionId }).IsUnique();

        builder.HasOne(r => r.Template)
               .WithMany(t => t.Requirements)
               .HasForeignKey(r => r.TemplateId)
               .OnDelete(DeleteBehavior.Cascade);

        // Die Funktion bleibt, solange ein Bedarf sie nennt -- abschalten
        // statt loeschen (ADR-0005).
        builder.HasOne(r => r.Function)
               .WithMany()
               .HasForeignKey(r => r.FunctionId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}

public class AbsenceConfiguration : IEntityTypeConfiguration<Absence>
{
    public void Configure(EntityTypeBuilder<Absence> builder)
    {
        builder.Property(a => a.Note).HasMaxLength(500);
        builder.Property(a => a.DecisionNote).HasMaxLength(500);

        // Die beiden Fragen: Was hat diese Person, und wer fehlt in diesem
        // Zeitraum?
        builder.HasIndex(a => new { a.ContactId, a.StartDate });
        builder.HasIndex(a => new { a.StartDate, a.EndDate });
        builder.HasIndex(a => a.Status);

        // Abwesenheiten sind Personalunterlagen: Ein Kontakt mit solchen
        // Eintraegen laesst sich nicht nebenbei loeschen.
        builder.HasOne(a => a.Contact)
               .WithMany()
               .HasForeignKey(a => a.ContactId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Deputy)
               .WithMany()
               .HasForeignKey(a => a.DeputyId)
               .OnDelete(DeleteBehavior.SetNull);
    }
}

public class EmploymentConfiguration : IEntityTypeConfiguration<Employment>
{
    public void Configure(EntityTypeBuilder<Employment> builder)
    {
        builder.Property(e => e.Note).HasMaxLength(500);
        builder.Property(e => e.WeeklyHours).HasPrecision(5, 2);
        builder.Property(e => e.AnnualLeaveDays).HasPrecision(5, 1);

        // Je Person und Tag ein Stand -- sonst bliebe offen, welcher gilt.
        builder.HasIndex(e => new { e.ContactId, e.ValidFrom }).IsUnique();

        builder.HasOne(e => e.Contact)
               .WithMany()
               .HasForeignKey(e => e.ContactId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}

public class LeaveAdjustmentConfiguration : IEntityTypeConfiguration<LeaveAdjustment>
{
    public void Configure(EntityTypeBuilder<LeaveAdjustment> builder)
    {
        builder.Property(a => a.Reason).HasMaxLength(200);
        builder.Property(a => a.Days).HasPrecision(5, 1);
        builder.HasIndex(a => new { a.ContactId, a.Year });

        builder.HasOne(a => a.Contact)
               .WithMany()
               .HasForeignKey(a => a.ContactId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}

public class HouseHolidayConfiguration : IEntityTypeConfiguration<HouseHoliday>
{
    public void Configure(EntityTypeBuilder<HouseHoliday> builder)
    {
        builder.Property(h => h.Name).HasMaxLength(100);
        builder.HasIndex(h => new { h.Month, h.Day }).IsUnique();
    }
}

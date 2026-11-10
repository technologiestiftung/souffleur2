using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souffleur.Domain.Catalogs;
using Souffleur.Domain.Contacts;
using Souffleur.Domain.Work;

namespace Souffleur.Infrastructure.Persistence.Configurations;

public class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
{
    public void Configure(EntityTypeBuilder<TaskItem> builder)
    {
        builder.Property(t => t.Title).HasMaxLength(200);
        builder.Property(t => t.Description).HasMaxLength(4000);

        // Die beiden Fragen, die die Liste stellt: Was ist offen, und was
        // steht als Nächstes an?
        builder.HasIndex(t => t.State);
        builder.HasIndex(t => t.DueDate);
        builder.HasIndex(t => t.AssigneeId);

        builder.HasOne(t => t.Parent)
               .WithMany(t => t.Children)
               .HasForeignKey(t => t.ParentId)
               .OnDelete(DeleteBehavior.Restrict);

        // Kategorie und Zuständigkeit bleiben, solange Aufgaben sie tragen --
        // abschalten statt löschen (ADR-0005).
        builder.HasOne(t => t.Category)
               .WithMany()
               .HasForeignKey(t => t.CategoryId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Assignee)
               .WithMany()
               .HasForeignKey(t => t.AssigneeId)
               .OnDelete(DeleteBehavior.Restrict);

        // Wird eine Gruppe geloescht, bleibt die Aufgabe -- nur ohne Gruppe.
        // Sie verschwindet nicht mit, und sie blockiert das Loeschen nicht.
        builder.HasIndex(t => t.GroupId);
        builder.HasOne(t => t.Group)
               .WithMany()
               .HasForeignKey(t => t.GroupId)
               .OnDelete(DeleteBehavior.SetNull);

        // Faellt die Produktion weg, bleibt die Aufgabe -- nur ohne Klammer.
        builder.HasIndex(t => t.ProductionId);
        builder.HasOne(t => t.Production)
               .WithMany()
               .HasForeignKey(t => t.ProductionId)
               .OnDelete(DeleteBehavior.SetNull);

        // Kostenstellen werden nicht geloescht, nur stillgelegt (ADR-0005);
        // sollte es doch geschehen, bleibt die Aufgabe ohne Kostenstelle.
        builder.HasIndex(t => t.CostCenterId);
        builder.HasOne(t => t.CostCenter)
               .WithMany()
               .HasForeignKey(t => t.CostCenterId)
               .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(t => t.Contacts)
               .WithOne(c => c.Task)
               .HasForeignKey(c => c.TaskId)
               .OnDelete(DeleteBehavior.Cascade);

        // Zwei Sichten auf dieselbe Sammlung, keine eigenen Beziehungen.
        builder.Ignore(t => t.Participants);
        builder.Ignore(t => t.RelatedContacts);
    }
}

public class TaskContactConfiguration : IEntityTypeConfiguration<TaskContact>
{
    public void Configure(EntityTypeBuilder<TaskContact> builder)
    {
        // Jeder Kontakt je Aufgabe und Art nur einmal.
        builder.HasIndex(c => new { c.TaskId, c.ContactId, c.Kind }).IsUnique();

        // Der Kontakt bleibt, solange Aufgaben ihn nennen -- stilllegen
        // statt loeschen, wie beim Zustaendigen.
        builder.HasOne(c => c.Contact)
               .WithMany()
               .HasForeignKey(c => c.ContactId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}

public class TaskDependencyConfiguration : IEntityTypeConfiguration<TaskDependency>
{
    public void Configure(EntityTypeBuilder<TaskDependency> builder)
    {
        // Dieselbe Verbindung nur einmal.
        builder.HasIndex(d => new { d.TaskId, d.DependsOnId }).IsUnique();

        // Verschwindet eine Aufgabe, verschwinden ihre Verbindungen mit --
        // sie haben ohne sie keinen Sinn. Nur eine Seite auf Cascade, sonst
        // lehnt SQLite die mehrfachen Löschwege ab.
        builder.HasOne(d => d.Task)
               .WithMany(t => t.DependsOn)
               .HasForeignKey(d => d.TaskId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(d => d.DependsOn)
               .WithMany(t => t.Blocks)
               .HasForeignKey(d => d.DependsOnId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(table => table.HasCheckConstraint(
            "CK_TaskDependency_NotSelf", "TaskId <> DependsOnId"));
    }
}

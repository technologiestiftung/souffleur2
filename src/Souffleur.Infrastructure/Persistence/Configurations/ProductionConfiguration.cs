using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souffleur.Domain.Productions;

namespace Souffleur.Infrastructure.Persistence.Configurations;

public class ProductionConfiguration : IEntityTypeConfiguration<Production>
{
    public void Configure(EntityTypeBuilder<Production> builder)
    {
        builder.Property(p => p.Title).HasMaxLength(200);
        builder.Property(p => p.Subtitle).HasMaxLength(200);
        builder.Property(p => p.Description).HasMaxLength(8000);
        builder.Property(p => p.Research).HasMaxLength(8000);
        builder.Property(p => p.Notes).HasMaxLength(4000);
        builder.Property(p => p.Budget).HasConversion<NullableMoneyConverter>();

        builder.Ignore(p => p.PlannedTotal);

        builder.HasIndex(p => p.Status);
        builder.HasIndex(p => p.PremiereDate);
    }
}

public class ProductionRoleConfiguration : IEntityTypeConfiguration<ProductionRole>
{
    public void Configure(EntityTypeBuilder<ProductionRole> builder)
    {
        builder.Property(r => r.Name).HasMaxLength(200);
        builder.Property(r => r.Note).HasMaxLength(500);
        builder.Ignore(r => r.IsCast);

        // Kein Unique auf (Produktion, Kontakt): Doppelbesetzung und Regie
        // plus Buehne in einer Hand sind Alltag.
        builder.HasIndex(r => r.ContactId);

        builder.HasOne(r => r.Production)
               .WithMany(p => p.Roles)
               .HasForeignKey(r => r.ProductionId)
               .OnDelete(DeleteBehavior.Cascade);

        // Der Kontakt bleibt, solange er irgendwo besetzt ist -- wie bei der
        // Beteiligung am Termin.
        builder.HasOne(r => r.Contact)
               .WithMany()
               .HasForeignKey(r => r.ContactId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Function)
               .WithMany()
               .HasForeignKey(r => r.FunctionId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ProductionBudgetLineConfiguration : IEntityTypeConfiguration<ProductionBudgetLine>
{
    public void Configure(EntityTypeBuilder<ProductionBudgetLine> builder)
    {
        builder.Property(l => l.Title).HasMaxLength(200);
        builder.Property(l => l.Category).HasMaxLength(100);
        builder.Property(l => l.Note).HasMaxLength(500);
        builder.Property(l => l.Amount).HasConversion<MoneyConverter>();

        builder.HasOne(l => l.Production)
               .WithMany(p => p.BudgetLines)
               .HasForeignKey(l => l.ProductionId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ProductionNeedConfiguration : IEntityTypeConfiguration<ProductionNeed>
{
    public void Configure(EntityTypeBuilder<ProductionNeed> builder)
    {
        builder.Property(n => n.Title).HasMaxLength(200);
        builder.Property(n => n.Quantity).HasMaxLength(100);
        builder.Property(n => n.Note).HasMaxLength(500);

        builder.HasOne(n => n.Production)
               .WithMany(p => p.Needs)
               .HasForeignKey(n => n.ProductionId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

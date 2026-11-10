using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souffleur.Domain.Accounting;

namespace Souffleur.Infrastructure.Persistence.Configurations;

public class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.Property(a => a.Number).HasMaxLength(20);
        builder.Property(a => a.Name).HasMaxLength(200);
        builder.Property(a => a.TaxKey).HasMaxLength(10);
        builder.HasIndex(a => a.Number).IsUnique();
    }
}

public class CostCenterConfiguration : IEntityTypeConfiguration<CostCenter>
{
    public void Configure(EntityTypeBuilder<CostCenter> builder)
    {
        builder.Property(c => c.Number).HasMaxLength(20);
        builder.Property(c => c.Name).HasMaxLength(200);
        builder.HasIndex(c => c.Number).IsUnique();
    }
}

public class JournalEntryConfiguration : IEntityTypeConfiguration<JournalEntry>
{
    public void Configure(EntityTypeBuilder<JournalEntry> builder)
    {
        builder.Property(e => e.Description).HasMaxLength(500);
        builder.Property(e => e.DocumentNumber).HasMaxLength(50);

        // Lueckenlos fortlaufend je Geschaeftsjahr -- die Datenbank haelt das fest,
        // nicht nur der Code, der die Nummer vergibt.
        builder.HasIndex(e => new { e.FiscalYear, e.Number }).IsUnique();
        builder.HasIndex(e => e.Date);

        builder.HasOne(e => e.ReversalOf)
               .WithMany()
               .HasForeignKey(e => e.ReversalOfId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(e => e.Lines)
               .WithOne(l => l.JournalEntry)
               .HasForeignKey(l => l.JournalEntryId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

public class JournalLineConfiguration : IEntityTypeConfiguration<JournalLine>
{
    public void Configure(EntityTypeBuilder<JournalLine> builder)
    {
        builder.Property(l => l.Debit).HasConversion<MoneyConverter>();
        builder.Property(l => l.Credit).HasConversion<MoneyConverter>();
        builder.Property(l => l.TaxAmount).HasConversion<MoneyConverter>();
        builder.Property(l => l.TaxKey).HasMaxLength(10);
        builder.HasIndex(l => l.TaxSphere);
        builder.Property(l => l.Description).HasMaxLength(500);

        builder.HasOne(l => l.Account).WithMany()
               .HasForeignKey(l => l.AccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(l => l.CostCenter).WithMany()
               .HasForeignKey(l => l.CostCenterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(l => l.Contact).WithMany()
               .HasForeignKey(l => l.ContactId).OnDelete(DeleteBehavior.Restrict);

        // Eine Zeile steht im Soll oder im Haben, nie in beidem und nie in keinem.
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_JournalLine_OneSideOnly",
            "(Debit > 0 AND Credit = 0) OR (Credit > 0 AND Debit = 0)"));
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souffleur.Domain.Accounting;
using Souffleur.Domain.Contacts;
using Souffleur.Domain.Productions;

namespace Souffleur.Infrastructure.Persistence.Configurations;

public class VoucherConfiguration : IEntityTypeConfiguration<Voucher>
{
    public void Configure(EntityTypeBuilder<Voucher> builder)
    {
        builder.Property(v => v.GrossAmount).HasConversion<MoneyConverter>();
        builder.Property(v => v.Description).HasMaxLength(500);
        builder.Property(v => v.ExternalNumber).HasMaxLength(100);

        // Lueckenlos fortlaufend je Jahr, wie die Journalnummer.
        builder.HasIndex(v => new { v.FiscalYear, v.Number }).IsUnique();
        builder.HasIndex(v => v.Date);

        builder.HasOne(v => v.Contact).WithMany()
               .HasForeignKey(v => v.ContactId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(v => v.JournalEntry).WithMany()
               .HasForeignKey(v => v.JournalEntryId).OnDelete(DeleteBehavior.Restrict);

        // Der Beleg bleibt acht Jahre; die Produktion nicht unbedingt. Faellt
        // sie weg, verliert der Beleg nur seine Zuordnung.
        builder.HasIndex(v => v.ProductionId);
        builder.HasOne<Production>().WithMany()
               .HasForeignKey(v => v.ProductionId).OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(v => v.File).WithOne(f => f.Voucher)
               .HasForeignKey<VoucherFile>(f => f.VoucherId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class VoucherFileConfiguration : IEntityTypeConfiguration<VoucherFile>
{
    public void Configure(EntityTypeBuilder<VoucherFile> builder)
    {
        builder.Property(f => f.RelativePath).HasMaxLength(400);
        builder.Property(f => f.OriginalName).HasMaxLength(255);
        builder.Property(f => f.ContentType).HasMaxLength(100);
        builder.Property(f => f.Sha256).HasMaxLength(64).IsFixedLength();
        builder.HasIndex(f => f.Sha256);
    }
}

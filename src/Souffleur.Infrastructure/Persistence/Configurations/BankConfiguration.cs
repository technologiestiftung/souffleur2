using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souffleur.Domain.Accounting;
using Souffleur.Domain.Banking;

namespace Souffleur.Infrastructure.Persistence.Configurations;

public class BankAccountConfiguration : IEntityTypeConfiguration<BankAccount>
{
    public void Configure(EntityTypeBuilder<BankAccount> builder)
    {
        builder.Property(a => a.Name).HasMaxLength(120);
        builder.Property(a => a.Iban).HasMaxLength(34);
        builder.Property(a => a.Bic).HasMaxLength(11);
        builder.Property(a => a.BankName).HasMaxLength(120);
        builder.Property(a => a.Balance).HasPrecision(18, 2);

        // Dasselbe Konto zweimal anzulegen hieße, Umsätze zu verdoppeln.
        builder.HasIndex(a => a.Iban).IsUnique();

        builder.HasOne(a => a.Account)
               .WithMany()
               .HasForeignKey(a => a.AccountId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}

public class BankTransactionConfiguration : IEntityTypeConfiguration<BankTransaction>
{
    public void Configure(EntityTypeBuilder<BankTransaction> builder)
    {
        builder.Property(t => t.Amount).HasPrecision(18, 2);
        builder.Property(t => t.Currency).HasMaxLength(3);
        builder.Property(t => t.Purpose).HasMaxLength(1000);
        builder.Property(t => t.CounterpartyName).HasMaxLength(200);
        builder.Property(t => t.CounterpartyIban).HasMaxLength(34);
        builder.Property(t => t.EndToEndId).HasMaxLength(100);
        builder.Property(t => t.BankReference).HasMaxLength(100);
        builder.Property(t => t.Fingerprint).HasMaxLength(400);
        builder.Property(t => t.Note).HasMaxLength(500);

        // Der Import fragt beides ab, bevor er etwas anlegt.
        builder.HasIndex(t => new { t.BankAccountId, t.BankReference });
        builder.HasIndex(t => new { t.BankAccountId, t.Fingerprint });
        builder.HasIndex(t => t.BookedOn);

        builder.HasOne(t => t.BankAccount)
               .WithMany(a => a.Transactions)
               .HasForeignKey(t => t.BankAccountId)
               .OnDelete(DeleteBehavior.Cascade);

        // Beleg und Buchung bleiben, auch wenn ein Umsatz darauf zeigt.
        builder.HasOne(t => t.Voucher)
               .WithMany()
               .HasForeignKey(t => t.VoucherId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.JournalEntry)
               .WithMany()
               .HasForeignKey(t => t.JournalEntryId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}

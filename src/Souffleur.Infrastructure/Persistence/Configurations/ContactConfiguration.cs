using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souffleur.Domain.Contacts;

namespace Souffleur.Infrastructure.Persistence.Configurations;

public class ContactConfiguration : IEntityTypeConfiguration<Contact>
{
    public void Configure(EntityTypeBuilder<Contact> builder)
    {
        // Ein Konto gehoert hoechstens einem Kontakt. Ohne diese Eindeutigkeit
        // waere "meine Zeiten" nicht entscheidbar.
        builder.HasIndex(c => c.UserId).IsUnique();

        builder.Property(c => c.Name).HasMaxLength(200);
        builder.Property(c => c.GivenName).HasMaxLength(100);
        builder.Property(c => c.Surname).HasMaxLength(100);
        builder.Property(c => c.FormOfAddress).HasMaxLength(60);
        builder.Property(c => c.Position).HasMaxLength(120);
        builder.Property(c => c.Email).HasMaxLength(254);
        builder.Property(c => c.Phone).HasMaxLength(40);
        builder.Property(c => c.Website).HasMaxLength(300);
        builder.Property(c => c.Street).HasMaxLength(200);
        builder.Property(c => c.PostalCode).HasMaxLength(10);
        builder.Property(c => c.City).HasMaxLength(100);
        builder.Property(c => c.Country).HasMaxLength(100);
        builder.Property(c => c.Notes).HasMaxLength(4000);
        builder.Property(c => c.Origin).HasMaxLength(200);

        builder.HasIndex(c => c.Name);
        builder.HasIndex(c => c.Email);

        // Eine Person kann zu einer Organisation gehören; gelöscht wird die
        // Organisation deshalb nicht, solange Personen an ihr hängen.
        builder.HasOne(c => c.OrganizationContact)
               .WithMany()
               .HasForeignKey(c => c.OrganizationContactId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(c => c.Roles)
               .WithOne(r => r.Contact)
               .HasForeignKey(r => r.ContactId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.BankDetails)
               .WithOne(b => b.Contact)
               .HasForeignKey<ContactBankDetails>(b => b.ContactId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ContactRoleAssignmentConfiguration : IEntityTypeConfiguration<ContactRoleAssignment>
{
    public void Configure(EntityTypeBuilder<ContactRoleAssignment> builder)
    {
        builder.Property(r => r.Note).HasMaxLength(300);

        // Jede Rolle höchstens einmal je Kontakt.
        builder.HasIndex(r => new { r.ContactId, r.Role }).IsUnique();
        builder.HasIndex(r => r.Role);
    }
}

public class ContactBankDetailsConfiguration : IEntityTypeConfiguration<ContactBankDetails>
{
    public void Configure(EntityTypeBuilder<ContactBankDetails> builder)
    {
        builder.Property(b => b.Iban).HasMaxLength(34);
        builder.Property(b => b.Bic).HasMaxLength(11);
        builder.Property(b => b.AccountHolder).HasMaxLength(200);
        builder.Property(b => b.VatId).HasMaxLength(20);
        builder.Property(b => b.TaxNumber).HasMaxLength(20);
        builder.Property(b => b.AccountNumber).HasMaxLength(20);
    }
}

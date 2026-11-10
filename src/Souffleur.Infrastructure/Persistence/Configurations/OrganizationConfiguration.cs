using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souffleur.Domain.Organizations;

namespace Souffleur.Infrastructure.Persistence.Configurations;

public class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.Property(o => o.Name).HasMaxLength(200);
        builder.Property(o => o.LegalForm).HasMaxLength(100);
        builder.Property(o => o.Street).HasMaxLength(200);
        builder.Property(o => o.PostalCode).HasMaxLength(10);
        builder.Property(o => o.City).HasMaxLength(100);
        builder.Property(o => o.TaxNumber).HasMaxLength(20);
        builder.Property(o => o.VatId).HasMaxLength(20);
        builder.Property(o => o.KskNumber).HasMaxLength(20);

        // souffleur ist nicht mandantenfaehig (ADR-0003): ein Haus je Installation.
        // Die feste Id macht aus der Tabelle eine Zeile -- die Datenbank laesst eine
        // zweite gar nicht erst zu, statt dass der Code sich darauf verlassen muss.
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_Organization_Singleton",
            $"Id = '{Organization.SingletonId}'"));
    }
}

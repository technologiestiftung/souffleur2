using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souffleur.Domain.Accounting;

namespace Souffleur.Infrastructure.Persistence.Configurations;

public class DatevExportConfiguration : IEntityTypeConfiguration<DatevExport>
{
    public void Configure(EntityTypeBuilder<DatevExport> builder)
    {
        builder.Property(e => e.FileName).HasMaxLength(200);
        builder.Property(e => e.Sha256).HasMaxLength(64).IsFixedLength();
        builder.Property(e => e.DebitTotal).HasConversion<MoneyConverter>();
        builder.HasIndex(e => new { e.From, e.To });
    }
}

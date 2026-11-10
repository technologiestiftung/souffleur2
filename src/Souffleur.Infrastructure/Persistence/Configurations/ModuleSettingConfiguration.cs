using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souffleur.Domain.Modules;

namespace Souffleur.Infrastructure.Persistence.Configurations;

public class ModuleSettingConfiguration : IEntityTypeConfiguration<ModuleSetting>
{
    public void Configure(EntityTypeBuilder<ModuleSetting> builder)
    {
        builder.Property(s => s.Note).HasMaxLength(500);

        // Ein Datensatz je Modul, nicht mehr.
        builder.HasIndex(s => s.Module).IsUnique();
    }
}

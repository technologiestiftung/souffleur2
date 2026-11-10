using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souffleur.Domain.Identity;

namespace Souffleur.Infrastructure.Persistence.Configurations;

public class UserPreferenceConfiguration : IEntityTypeConfiguration<UserPreference>
{
    public void Configure(EntityTypeBuilder<UserPreference> builder)
    {
        builder.Property(p => p.Key).HasMaxLength(100);

        // Grosszuegig, aber nicht unbegrenzt: Eine Reihenfolge ist eine Liste
        // von Schluesseln, keine Ablage.
        builder.Property(p => p.Value).HasMaxLength(4000);

        // Ein Datensatz je Person und Schluessel.
        builder.HasIndex(p => new { p.UserId, p.Key }).IsUnique();

        // Geht das Konto, gehen seine Einstellungen mit. Sie haben ohne die
        // Person keinen Sinn, und niemand wird sie vermissen.
        builder.HasOne<User>()
               .WithMany()
               .HasForeignKey(p => p.UserId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

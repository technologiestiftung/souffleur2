using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souffleur.Domain.Documents;
using Souffleur.Domain.Identity;

namespace Souffleur.Infrastructure.Persistence.Configurations;

public class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.Property(d => d.Name).HasMaxLength(200);
        builder.Property(d => d.OriginalName).HasMaxLength(255);
        builder.Property(d => d.ContentType).HasMaxLength(100);
        builder.Property(d => d.StoredAs).HasMaxLength(100);
        builder.Property(d => d.Sha256).HasMaxLength(64);
        builder.Property(d => d.Note).HasMaxLength(2000);

        builder.HasIndex(d => d.Sha256);

        // Eine Datei im Ordner ist ein Dokument. Der Index faengt den Fall,
        // dass Einlesen und Hochladen dieselbe Datei gleichzeitig anlegen.
        builder.Property(d => d.FolderPath).HasMaxLength(1000);
        builder.HasIndex(d => new { d.FolderId, d.FolderPath })
               .IsUnique()
               .HasFilter("\"FolderId\" IS NOT NULL");

        builder.HasOne(d => d.Folder)
               .WithMany()
               .HasForeignKey(d => d.FolderId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(d => d.Tags).WithOne().HasForeignKey(t => t.DocumentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(d => d.Links).WithOne().HasForeignKey(l => l.DocumentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(d => d.Groups).WithOne().HasForeignKey(g => g.DocumentId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class DocumentTagConfiguration : IEntityTypeConfiguration<DocumentTag>
{
    public void Configure(EntityTypeBuilder<DocumentTag> builder)
    {
        builder.Property(t => t.Tag).HasMaxLength(50);
        builder.HasIndex(t => new { t.DocumentId, t.Tag }).IsUnique();

        // Fuer „alle Dokumente mit diesem Schlagwort".
        builder.HasIndex(t => t.Tag);
    }
}

public class DocumentLinkConfiguration : IEntityTypeConfiguration<DocumentLink>
{
    public void Configure(EntityTypeBuilder<DocumentLink> builder)
    {
        builder.HasIndex(l => new { l.DocumentId, l.Target, l.TargetId }).IsUnique();

        // Fuer „welche Dokumente haengen an diesem Kontakt".
        builder.HasIndex(l => new { l.Target, l.TargetId });
    }
}

public class DocumentGroupConfiguration : IEntityTypeConfiguration<DocumentGroup>
{
    public void Configure(EntityTypeBuilder<DocumentGroup> builder)
    {
        builder.HasIndex(g => new { g.DocumentId, g.GroupId }).IsUnique();

        // Geht die Gruppe, geht nur ihr Eintrag. Das Dokument bleibt
        // eingeschraenkt (Document.IsRestricted) -- es wird nicht offen,
        // nur weil niemand mehr in der Liste steht.
        builder.HasOne(g => g.Group)
               .WithMany()
               .HasForeignKey(g => g.GroupId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

public class DocumentFolderConfiguration : IEntityTypeConfiguration<DocumentFolder>
{
    public void Configure(EntityTypeBuilder<DocumentFolder> builder)
    {
        builder.Property(f => f.Name).HasMaxLength(100);
        builder.Property(f => f.Path).HasMaxLength(500);
        builder.Property(f => f.LastScanResult).HasMaxLength(500);

        // Derselbe Ordner zweimal waere dieselbe Datei als zwei Dokumente.
        builder.HasIndex(f => f.Path).IsUnique();

        builder.HasMany(f => f.Groups).WithOne().HasForeignKey(g => g.FolderId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class DocumentFolderGroupConfiguration : IEntityTypeConfiguration<DocumentFolderGroup>
{
    public void Configure(EntityTypeBuilder<DocumentFolderGroup> builder)
    {
        builder.HasIndex(g => new { g.FolderId, g.GroupId }).IsUnique();

        builder.HasOne(g => g.Group)
               .WithMany()
               .HasForeignKey(g => g.GroupId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

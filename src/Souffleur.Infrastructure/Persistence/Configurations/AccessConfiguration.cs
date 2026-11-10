using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souffleur.Domain.Identity;

namespace Souffleur.Infrastructure.Persistence.Configurations;

public class GroupConfiguration : IEntityTypeConfiguration<Group>
{
    public void Configure(EntityTypeBuilder<Group> builder)
    {
        builder.Property(g => g.Name).HasMaxLength(100);
        builder.Property(g => g.Note).HasMaxLength(500);

        // Zwei Gruppen „Technik" waeren beim Zuweisen nicht zu unterscheiden.
        builder.HasIndex(g => g.Name).IsUnique();

        builder.HasMany(g => g.Members)
               .WithOne()
               .HasForeignKey(m => m.GroupId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(g => g.Roles)
               .WithOne()
               .HasForeignKey(r => r.GroupId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

public class GroupMemberConfiguration : IEntityTypeConfiguration<GroupMember>
{
    public void Configure(EntityTypeBuilder<GroupMember> builder)
    {
        builder.HasIndex(m => new { m.GroupId, m.UserId }).IsUnique();

        // Geht das Konto, geht die Mitgliedschaft mit.
        builder.HasOne<User>()
               .WithMany()
               .HasForeignKey(m => m.UserId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

public class GroupRoleConfiguration : IEntityTypeConfiguration<GroupRole>
{
    public void Configure(EntityTypeBuilder<GroupRole> builder)
    {
        builder.HasIndex(r => new { r.GroupId, r.RoleId }).IsUnique();

        builder.HasOne<Role>()
               .WithMany()
               .HasForeignKey(r => r.RoleId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

public class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.HasIndex(p => new { p.RoleId, p.Permission }).IsUnique();

        builder.HasOne<Role>()
               .WithMany(r => r.Permissions)
               .HasForeignKey(p => p.RoleId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

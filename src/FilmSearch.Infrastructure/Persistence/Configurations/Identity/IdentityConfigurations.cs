using FilmSearch.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FilmSearch.Infrastructure.Persistence.Configurations.Identity;

// ASP.NET Core Identity tables (I17) in the "identity" schema with short snake_case names. Columns and keys come from
// IdentityDbContext; only table and index names change here.

internal static class IdentitySchema
{
    public const string Name = "identity";
}

internal sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.ToTable("users", IdentitySchema.Name);
        builder.HasIndex(u => u.NormalizedUserName).HasDatabaseName("ix_users_normalized_user_name");
        builder.HasIndex(u => u.NormalizedEmail).HasDatabaseName("ix_users_normalized_email");
    }
}

internal sealed class RoleConfiguration : IEntityTypeConfiguration<IdentityRole<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityRole<Guid>> builder)
    {
        builder.ToTable("roles", IdentitySchema.Name);
        builder.HasIndex(r => r.NormalizedName).HasDatabaseName("ix_roles_normalized_name");
    }
}

internal sealed class UserRoleConfiguration : IEntityTypeConfiguration<IdentityUserRole<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserRole<Guid>> builder) => builder.ToTable("user_roles", IdentitySchema.Name);
}

internal sealed class UserClaimConfiguration : IEntityTypeConfiguration<IdentityUserClaim<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserClaim<Guid>> builder) => builder.ToTable("user_claims", IdentitySchema.Name);
}

internal sealed class UserLoginConfiguration : IEntityTypeConfiguration<IdentityUserLogin<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserLogin<Guid>> builder) => builder.ToTable("user_logins", IdentitySchema.Name);
}

internal sealed class UserTokenConfiguration : IEntityTypeConfiguration<IdentityUserToken<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserToken<Guid>> builder) => builder.ToTable("user_tokens", IdentitySchema.Name);
}

internal sealed class RoleClaimConfiguration : IEntityTypeConfiguration<IdentityRoleClaim<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityRoleClaim<Guid>> builder)
    {
        builder.ToTable("role_claims", IdentitySchema.Name);
        // The same relationship as Identity's; only the constraint name (the convention used the old table name).
        builder.HasOne<IdentityRole<Guid>>().WithMany().HasForeignKey(c => c.RoleId).HasConstraintName("fk_role_claims_roles_role_id");
    }
}

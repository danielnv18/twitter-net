using api.Auth;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityUserContext<AppUser, Guid>(options), IDataProtectionKeyContext
{
    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<AppUser>(user =>
        {
            user.Property(x => x.Name).HasMaxLength(100);
            user.Property(x => x.UserName).HasMaxLength(30);
            user.Property(x => x.NormalizedUserName).HasMaxLength(30).UseCollation("utf8mb4_bin");
            user.Property(x => x.Email).HasMaxLength(254);
            user.Property(x => x.NormalizedEmail).HasMaxLength(254).UseCollation("utf8mb4_bin");
            user.HasIndex(x => x.NormalizedEmail).IsUnique().HasDatabaseName("EmailIndex");
        });
        builder.Entity<AuthSession>(session =>
        {
            session.Property(x => x.Id).HasMaxLength(64).IsFixedLength().UseCollation("utf8mb4_bin");
            session.Property(x => x.SecurityStamp).HasMaxLength(256);
            session.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            session.HasIndex(x => x.ExpiresAtUtc);
        });
    }
}

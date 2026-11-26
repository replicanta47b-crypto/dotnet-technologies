using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Auth.Api.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(e => e.Property(u => u.FullName).HasMaxLength(200));

        builder.Entity<RefreshToken>(e =>
        {
            e.HasKey(t => t.Id);
            e.HasIndex(t => t.TokenHash).IsUnique();
            e.HasIndex(t => t.UserId);
            e.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();   // hex SHA-256
            e.Property(t => t.ReplacedByTokenHash).HasMaxLength(64);
            e.Property(t => t.CreatedByIp).HasMaxLength(64);
            e.Property(t => t.RevokedByIp).HasMaxLength(64);
            e.Property(t => t.ReasonRevoked).HasMaxLength(200);
            e.Ignore(t => t.IsRevoked).Ignore(t => t.IsExpired).Ignore(t => t.IsActive);
            e.HasOne(t => t.User).WithMany(u => u.RefreshTokens).HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}

/// <summary>Для `dotnet ef migrations add` без запуску застосунку (рядок підключення не використовується — лише провайдер).</summary>
public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=.;Database=IdentityDb;Trusted_Connection=True;TrustServerCertificate=True")
            .Options);
}

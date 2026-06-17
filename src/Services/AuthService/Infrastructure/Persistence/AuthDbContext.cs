using AuthService.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Infrastructure.Persistence;

public class AuthDbContext : DbContext
{
    // Constructor: receives options (connection string etc.) from DI and passes to base
    public AuthDbContext(DbContextOptions<AuthDbContext> options) : base(options) { }

    // Represents the "Users" table in PostgreSQL
    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            // Primary key - EF Core also auto-detects "Id" but being explicit is clearer
            entity.HasKey(e => e.Id);

            // Email must be unique - no two users can share the same email
            entity.HasIndex(e => e.Email).IsUnique();

            // Column constraints
            entity.Property(e => e.Email).IsRequired().HasMaxLength(256);
            entity.Property(e => e.Username).IsRequired().HasMaxLength(100);
            entity.Property(e => e.PasswordHash).IsRequired();
            entity.Property(e => e.Role).IsRequired().HasDefaultValue("User");
        });
    }
}

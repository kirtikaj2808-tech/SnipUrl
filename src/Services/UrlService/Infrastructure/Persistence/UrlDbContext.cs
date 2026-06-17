using Microsoft.EntityFrameworkCore;
using UrlService.Domain.Entities;

namespace UrlService.Infrastructure.Persistence;

public class UrlDbContext : DbContext
{
    public UrlDbContext(DbContextOptions<UrlDbContext> options) : base(options) { }

    // This represents the ShortUrls table in PostgreSQL
    public DbSet<ShortUrl> ShortUrls => Set<ShortUrl>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ShortUrl>(entity =>
        {
            entity.HasKey(e => e.Id);

            // ShortCode must be unique - no two links can have same code
            entity.HasIndex(e => e.ShortCode).IsUnique();

            // CustomAlias must also be unique if provided
            entity.HasIndex(e => e.CustomAlias).IsUnique();

            entity.Property(e => e.OriginalUrl).IsRequired().HasMaxLength(2048);
            entity.Property(e => e.ShortCode).IsRequired().HasMaxLength(20);
            entity.Property(e => e.CustomAlias).HasMaxLength(50);
        });
    }
}
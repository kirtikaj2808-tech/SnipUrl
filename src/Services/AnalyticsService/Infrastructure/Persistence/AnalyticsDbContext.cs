using AnalyticsService.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AnalyticsService.Infrastructure.Persistence;

public class AnalyticsDbContext : DbContext
{
    public AnalyticsDbContext(DbContextOptions<AnalyticsDbContext> options) : base(options) { }

    public DbSet<ClickEvent> ClickEvents => Set<ClickEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ClickEvent>(entity =>
        {
            entity.HasKey(e => e.Id);

            // Index on ShortCode — we'll query "all clicks for this code" a lot
            entity.HasIndex(e => e.ShortCode);

            // Index on ClickedAt — needed for time-series charts (clicks per day)
            entity.HasIndex(e => e.ClickedAt);

            entity.Property(e => e.ShortCode).IsRequired().HasMaxLength(20);
            entity.Property(e => e.OriginalUrl).HasMaxLength(2048);
        });
    }
}
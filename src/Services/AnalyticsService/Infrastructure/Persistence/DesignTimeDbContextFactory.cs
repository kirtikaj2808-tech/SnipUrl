using AnalyticsService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AnalyticsService.Infrastructure.Persistence;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AnalyticsDbContext>
{
    public AnalyticsDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AnalyticsDbContext>();
        
        var connectionString = "Host=localhost;Port=5432;Database=snipurl_analytics;Username=snipurl;Password=snipurl123";
        
        optionsBuilder.UseNpgsql(connectionString);
        
        return new AnalyticsDbContext(optionsBuilder.Options);
    }
}

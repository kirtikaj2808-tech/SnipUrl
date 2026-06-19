using AnalyticsService.Domain.Entities;
using AnalyticsService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ── 1. Add PostgreSQL via EF Core ─────────────────────────────────────
builder.Services.AddDbContext<AnalyticsDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// ── 2. Add OpenAPI ─────────────────────────────────────────────────────
builder.Services.AddOpenApi();

var app = builder.Build();

// ── 4. Auto-run migrations on startup ───────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();
    db.Database.Migrate();
}

// ── 5. Configure the HTTP request pipeline ─────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// ── 6. Analytics endpoints ─────────────────────────────────────────────
app.MapPost("/api/analytics/clicks", async (ClickEventRequest request, AnalyticsDbContext db) =>
{
    var clickEvent = new ClickEvent
    {
        ShortCode = request.ShortCode,
        OriginalUrl = request.OriginalUrl,
        ClickedAt = request.ClickedAt,
        IpAddress = request.IpAddress,
        UserAgent = request.UserAgent,
        Referrer = request.Referrer,
        DeviceType = request.DeviceType
    };

    db.ClickEvents.Add(clickEvent);
    await db.SaveChangesAsync();

    Console.WriteLine($"[Analytics] Click event saved: {request.ShortCode} at {request.ClickedAt}");
    return Results.Ok();
})
.WithName("RecordClick")
.WithSummary("Record a click event");

app.MapGet("/api/analytics/clicks/{shortCode}", async (string shortCode, AnalyticsDbContext db) =>
{
    var clicks = await db.ClickEvents
        .Where(e => e.ShortCode == shortCode)
        .OrderByDescending(e => e.ClickedAt)
        .ToListAsync();

    return Results.Ok(new
    {
        shortCode,
        totalClicks = clicks.Count,
        clicks
    });
})
.WithName("GetClickAnalytics")
.WithSummary("Get click analytics for a short URL");

app.MapGet("/api/analytics/summary", async (AnalyticsDbContext db) =>
{
    var summary = await db.ClickEvents
        .GroupBy(e => e.ShortCode)
        .Select(g => new
        {
            shortCode = g.Key,
            totalClicks = g.Count(),
            lastClicked = g.Max(e => e.ClickedAt)
        })
        .ToListAsync();

    return Results.Ok(summary);
})
.WithName("GetAnalyticsSummary")
.WithSummary("Get analytics summary for all short URLs");

app.Run();

// Request DTO for receiving click events
public record ClickEventRequest(
    string ShortCode,
    string OriginalUrl,
    DateTime ClickedAt,
    string? IpAddress,
    string? UserAgent,
    string? Referrer,
    string DeviceType
);

using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using StackExchange.Redis;
using UrlService.Endpoints;
using UrlService.Infrastructure.Cache;
using UrlService.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// ── 1. Add OpenAPI (for Scalar UI) ────────────────────────────────────
builder.Services.AddOpenApi();

// ── 2. Add PostgreSQL via EF Core ─────────────────────────────────────
builder.Services.AddDbContext<UrlDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

 
// ── 3. Redis ───────────────────────────────────────────────────────────
// IConnectionMultiplexer is the main Redis connection object
// It's registered as a Singleton — one shared connection for the whole app
// (Multiplexer is designed to be shared, creating one per request is wasteful)
var redisConnection = builder.Configuration.GetConnectionString("Redis")
    ?? "localhost:6379";
 
builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect(redisConnection));
 
// Register our cache service so it can be injected into endpoints
builder.Services.AddScoped<RedisCacheService>();
 
var app = builder.Build();

// ── 3. Auto-run migrations on startup ─────────────────────────────────
// This creates the database tables automatically when the app starts
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<UrlDbContext>();
    db.Database.Migrate();
}

// ── 4. Enable Scalar API UI in development ────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// ── 5. Map our endpoints ──────────────────────────────────────────────
app.MapShortenEndpoint();
app.MapRedirectEndpoint();

app.Run();
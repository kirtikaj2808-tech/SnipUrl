using Microsoft.EntityFrameworkCore;
using UrlService.Infrastructure.Cache;
using UrlService.Infrastructure.Persistence;

namespace UrlService.Endpoints;

public static class RedirectEndpoint
{
    public static void MapRedirectEndpoint(this WebApplication app)
    {
        app.MapGet("/{code}", async (
            string code,
            RedisCacheService cache,   // injected by .NET automatically
            UrlDbContext db,
            IHttpClientFactory httpClientFactory,
            HttpContext context) =>
        {
            // ── STEP 1: Check Redis first (cache-aside pattern) ──────────
            // This is the fast path — if the URL is cached, we never touch
            // the database at all. Redis responds in ~0.1ms vs ~10ms for DB.
            var cachedUrl = await cache.GetUrlAsync(code);

            if (cachedUrl is not null)
            {
                // Cache HIT — publish analytics event and return immediately
                _ = PublishClickEvent(httpClientFactory, code, cachedUrl, context);
                return Results.Redirect(cachedUrl);
            }

            // ── STEP 2: Cache MISS — go to the database ──────────────────
            // This only happens on the FIRST request for this short code.
            // Every request after that will hit the cache instead.
            var shortUrl = await db.ShortUrls
                .FirstOrDefaultAsync(u => u.ShortCode == code || u.CustomAlias == code);

            // ── STEP 3: Handle not found ──────────────────────────────────
            if (shortUrl is null)
                return Results.NotFound($"Short URL '{code}' not found.");

            // ── STEP 4: Check if link is still valid ──────────────────────
            if (!shortUrl.IsActive)
                return Results.BadRequest("This link has been deactivated.");

            if (shortUrl.ExpiresAt.HasValue && shortUrl.ExpiresAt.Value < DateTime.UtcNow)
                return Results.BadRequest("This link has expired.");

            // ── STEP 5: Save to Redis for next time ───────────────────────
            // This is the "populate cache" step of cache-aside.
            // Next request for this code will get it from Redis, not the DB.
            await cache.SetUrlAsync(code, shortUrl.OriginalUrl);

            // ── STEP 6: Increment click count and redirect ─────────────────
            // Note: we do this AFTER caching so the cache doesn't need
            // to know about click counts — it only stores the URL string.
            shortUrl.ClickCount++;
            await db.SaveChangesAsync();

            // ── STEP 7: Publish analytics event (fire and forget) ───────────
            _ = PublishClickEvent(httpClientFactory, code, shortUrl.OriginalUrl, context);

            return Results.Redirect(shortUrl.OriginalUrl);
        })
        .WithName("RedirectToUrl")
        .WithSummary("Redirect to original URL")
        .WithDescription("Checks Redis cache first, falls back to PostgreSQL on miss")
        .Produces(StatusCodes.Status302Found)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status400BadRequest);
    }

    private static async Task PublishClickEvent(
        IHttpClientFactory httpClientFactory,
        string shortCode,
        string originalUrl,
        HttpContext context)
    {
        try
        {
            var httpClient = httpClientFactory.CreateClient();
            var clickEvent = new
            {
                shortCode,
                originalUrl,
                clickedAt = DateTime.UtcNow,
                ipAddress = context.Connection.RemoteIpAddress?.ToString(),
                userAgent = context.Request.Headers["User-Agent"].ToString(),
                referrer = context.Request.Headers["Referer"].ToString(),
                deviceType = DetectDeviceType(context.Request.Headers["User-Agent"].ToString())
            };

            await httpClient.PostAsJsonAsync("http://localhost:5268/api/analytics/clicks", clickEvent);
        }
        catch (Exception ex)
        {
            // Log error but don't fail the redirect
            Console.WriteLine($"[Analytics] Failed to publish click event: {ex.Message}");
        }
    }

    private static string DetectDeviceType(string userAgent)
    {
        if (string.IsNullOrEmpty(userAgent))
            return "Unknown";

        var ua = userAgent.ToLower();
        if (ua.Contains("mobile") || ua.Contains("android") || ua.Contains("iphone"))
            return "Mobile";
        if (ua.Contains("tablet") || ua.Contains("ipad"))
            return "Tablet";
        return "Desktop";
    }
}
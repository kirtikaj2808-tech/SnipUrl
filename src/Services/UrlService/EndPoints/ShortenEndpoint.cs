using Microsoft.EntityFrameworkCore;
using UrlService.Common;
using UrlService.Domain.Entities;
using UrlService.Infrastructure.Persistence;

namespace UrlService.Endpoints;

public static class ShortenEndpoint
{
    public static void MapShortenEndpoint(this WebApplication app)
    {
        app.MapPost("/shorten", async (ShortenUrlRequest request, UrlDbContext db) =>
        {
            // Step 1: Validate the URL
            if (string.IsNullOrWhiteSpace(request.Url))
                return Results.BadRequest("URL cannot be empty.");

            if (!Uri.TryCreate(request.Url, UriKind.Absolute, out _))
                return Results.BadRequest("Please provide a valid URL including http:// or https://");

            // Step 2: Handle custom alias if provided
            if (!string.IsNullOrWhiteSpace(request.CustomAlias))
            {
                // Check if alias is already taken
                var aliasExists = await db.ShortUrls
                    .AnyAsync(u => u.CustomAlias == request.CustomAlias);

                if (aliasExists)
                    return Results.Conflict($"The alias '{request.CustomAlias}' is already taken. Please choose another.");
            }

            // Step 3: Create the ShortUrl record
            var shortUrl = new ShortUrl
            {
                OriginalUrl = request.Url,
                CustomAlias = request.CustomAlias,
                ExpiresAt = request.ExpiresAt,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };

            // Step 4: Save first to get the auto-generated Id
            db.ShortUrls.Add(shortUrl);
            await db.SaveChangesAsync();

            // Step 5: Generate short code from Id using Base62
            // If custom alias provided use that, otherwise generate code
            shortUrl.ShortCode = !string.IsNullOrWhiteSpace(request.CustomAlias)
                ? request.CustomAlias
                : Base62Encoder.Encode(shortUrl.Id);

            await db.SaveChangesAsync();

            // Step 6: Build the short URL and return it
            var baseUrl = app.Configuration["BaseUrl"] ?? "http://localhost:5132";
            var shortLink = $"{baseUrl}/{shortUrl.ShortCode}";

            return Results.Ok(new ShortenUrlResponse(
                ShortUrl: shortLink,
                ShortCode: shortUrl.ShortCode,
                OriginalUrl: shortUrl.OriginalUrl,
                CreatedAt: shortUrl.CreatedAt,
                ExpiresAt: shortUrl.ExpiresAt
            ));
        })
        .WithName("ShortenUrl")
        .WithSummary("Shorten a long URL")
        .WithDescription("Takes a long URL and returns a shortened version")
        .Produces<ShortenUrlResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status409Conflict);
    }
}

// What the user sends in the request body
public record ShortenUrlRequest(
    string Url,
    string? CustomAlias,
    DateTime? ExpiresAt
);

// What we send back to the user
public record ShortenUrlResponse(
    string ShortUrl,
    string ShortCode,
    string OriginalUrl,
    DateTime CreatedAt,
    DateTime? ExpiresAt
);
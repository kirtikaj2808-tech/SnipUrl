namespace UrlService.Domain.Entities;

public class ShortUrl
{
    public int Id { get; set; }

    // The original long URL e.g. https://www.google.com/very/long/path
    public string OriginalUrl { get; set; } = string.Empty;

    // The short code e.g. "abc123"
    public string ShortCode { get; set; } = string.Empty;

    // Optional custom alias e.g. "my-link"
    public string? CustomAlias { get; set; }

    // How many times this link was clicked
    public int ClickCount { get; set; } = 0;

    // When this link was created
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Optional expiry date - null means never expires
    public DateTime? ExpiresAt { get; set; }

    // Is this link still active?
    public bool IsActive { get; set; } = true;
}
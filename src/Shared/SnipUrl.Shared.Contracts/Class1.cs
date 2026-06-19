namespace SnipUrl.Shared.Contracts;

public class UrlClickedEvent
{
    public string ShortCode { get; set; } = string.Empty;
    public string OriginalUrl { get; set; } = string.Empty;
    public DateTime ClickedAt { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? Referrer { get; set; }
    public string DeviceType { get; set; } = "Unknown";
}

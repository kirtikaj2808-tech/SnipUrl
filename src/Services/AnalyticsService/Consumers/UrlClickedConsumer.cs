using AnalyticsService.Domain.Entities;
using AnalyticsService.Infrastructure.Persistence;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using SnipUrl.Shared.Contracts;

namespace AnalyticsService.Consumers;

public class UrlClickedConsumer : IConsumer<UrlClickedEvent>
{
    private readonly AnalyticsDbContext _db;

    public UrlClickedConsumer(AnalyticsDbContext db)
    {
        _db = db;
    }

    public async Task Consume(ConsumeContext<UrlClickedEvent> context)
    {
        var message = context.Message;

        var clickEvent = new ClickEvent
        {
            ShortCode = message.ShortCode,
            OriginalUrl = message.OriginalUrl,
            ClickedAt = message.ClickedAt,
            IpAddress = message.IpAddress,
            UserAgent = message.UserAgent,
            Referrer = message.Referrer,
            DeviceType = message.DeviceType
        };

        _db.ClickEvents.Add(clickEvent);
        await _db.SaveChangesAsync();

        Console.WriteLine($"[Analytics] Click event saved: {message.ShortCode} at {message.ClickedAt}");
    }
}
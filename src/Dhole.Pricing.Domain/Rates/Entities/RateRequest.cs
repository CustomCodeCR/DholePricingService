using CustomCodeFramework.Core.Domain.Entities;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.Domain.Rates.Entities;

public sealed class RateRequest : Entity<Guid>
{
    private RateRequest() { }

    private RateRequest(
        Guid id,
        RateRequestPriority priority,
        Guid? sellerUserId,
        string? sellerName,
        string? sellerEmail,
        string? clientName,
        string? executiveName,
        string? shipmentMode,
        string? originName,
        string? destinationName,
        string payloadJson
    ) : base(id)
    {
        Priority = priority;
        Status = RateRequestStatus.Open;
        RequestedAtUtc = DateTime.UtcNow;
        DueAtUtc = RequestedAtUtc.AddHours(priority switch
        {
            RateRequestPriority.Green => 24,
            RateRequestPriority.Yellow => 48,
            RateRequestPriority.Red => 72,
            _ => 72,
        });
        SellerUserId = sellerUserId;
        SellerName = Normalize(sellerName);
        SellerEmail = Normalize(sellerEmail);
        ClientName = Normalize(clientName);
        ExecutiveName = Normalize(executiveName);
        ShipmentMode = Normalize(shipmentMode);
        OriginName = Normalize(originName);
        DestinationName = Normalize(destinationName);
        PayloadJson = string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson;
    }

    public RateRequestPriority Priority { get; private set; }
    public RateRequestStatus Status { get; private set; }
    public DateTime RequestedAtUtc { get; private set; }
    public DateTime DueAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public DateTime? SlaReminderSentAtUtc { get; private set; }
    public Guid? RateId { get; private set; }
    public Guid? SellerUserId { get; private set; }
    public string? SellerName { get; private set; }
    public string? SellerEmail { get; private set; }
    public string? ClientName { get; private set; }
    public string? ExecutiveName { get; private set; }
    public string? ShipmentMode { get; private set; }
    public string? OriginName { get; private set; }
    public string? DestinationName { get; private set; }
    public string PayloadJson { get; private set; } = "{}";

    public static RateRequest Create(
        RateRequestPriority priority,
        Guid? sellerUserId,
        string? sellerName,
        string? sellerEmail,
        string? clientName,
        string? executiveName,
        string? shipmentMode,
        string? originName,
        string? destinationName,
        string payloadJson
    ) => new(
        Guid.NewGuid(),
        priority,
        sellerUserId,
        sellerName,
        sellerEmail,
        clientName,
        executiveName,
        shipmentMode,
        originName,
        destinationName,
        payloadJson
    );

    public void AttachRate(Guid rateId)
    {
        if (rateId == Guid.Empty)
            throw new InvalidOperationException("La tarifa asociada es requerida.");

        RateId = rateId;
    }

    public void MarkCompleted(DateTime completedAtUtc)
    {
        if (Status != RateRequestStatus.Open) return;
        Status = RateRequestStatus.Completed;
        CompletedAtUtc = completedAtUtc;
    }

    public void MarkSlaReminderSent(DateTime sentAtUtc)
    {
        SlaReminderSentAtUtc = sentAtUtc;
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

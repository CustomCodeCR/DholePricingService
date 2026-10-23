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
            RateRequestPriority.Green => 72,
            RateRequestPriority.Yellow => 48,
            RateRequestPriority.Red => 24,
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
    // Estado operativo independiente del ciclo de vida Open/Completed/Cancelled.
    public string? WorkStatus { get; private set; }
    public DateTime? WorkStatusUpdatedAtUtc { get; private set; }
    public DateTime RequestedAtUtc { get; private set; }
    public DateTime DueAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public Guid? CancelledByUserId { get; private set; }
    public string? CancelledByName { get; private set; }
    public string? CancellationReason { get; private set; }
    public string? CancellationDetails { get; private set; }
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
    public Guid? PoeId { get; private set; }
    public string? PoeName { get; private set; }
    public Guid? PodId { get; private set; }
    public string? PodName { get; private set; }
    public string PayloadJson { get; private set; } = "{}";
    public Guid? LockedByUserId { get; private set; }
    public string? LockedByName { get; private set; }
    public Guid? LockToken { get; private set; }
    public DateTime? LockedAtUtc { get; private set; }
    public DateTime? LockExpiresAtUtc { get; private set; }

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

    public void UpdateSubmission(
        RateRequestPriority priority,
        string? clientName,
        string? executiveName,
        string? shipmentMode,
        string? originName,
        string? destinationName,
        string payloadJson)
    {
        if (Status != RateRequestStatus.Open || RateId.HasValue)
            throw new InvalidOperationException("La solicitud ya no está disponible para edición.");

        if (Priority != priority)
        {
            Priority = priority;
            // Keep the original submission date so editing cannot reset the SLA clock.
            DueAtUtc = RequestedAtUtc.AddHours(priority switch
            {
                RateRequestPriority.Green => 72,
                RateRequestPriority.Yellow => 48,
                RateRequestPriority.Red => 24,
                _ => 72,
            });
            SlaReminderSentAtUtc = null;
        }

        ClientName = Normalize(clientName);
        ExecutiveName = Normalize(executiveName);
        ShipmentMode = Normalize(shipmentMode);
        OriginName = Normalize(originName);
        DestinationName = Normalize(destinationName);
        PayloadJson = string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson;
    }

    public void SetRoute(Guid? poeId, string? poeName, Guid? podId, string? podName)
    {
        PoeId = poeId;
        PoeName = Normalize(poeName);
        PodId = podId;
        PodName = Normalize(podName);
    }

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

    public void MarkCancelled(string reason, string? details, Guid actorId, string actorName, DateTime cancelledAtUtc)
    {
        if (Status != RateRequestStatus.Open)
            throw new InvalidOperationException("Sólo pueden cancelarse solicitudes abiertas.");

        Status = RateRequestStatus.Cancelled;
        CancelledAtUtc = cancelledAtUtc;
        CancelledByUserId = actorId;
        CancelledByName = Normalize(actorName);
        CancellationReason = reason;
        CancellationDetails = Normalize(details);

        // A cancelled request can never retain an active work lease.
        LockedByUserId = null;
        LockedByName = null;
        LockToken = null;
        LockedAtUtc = null;
        LockExpiresAtUtc = null;
    }

    public void SetWorkStatus(string workStatus, DateTime updatedAtUtc)
    {
        if (Status != RateRequestStatus.Open)
            throw new InvalidOperationException("Sólo puede actualizarse el avance de una solicitud abierta.");
        if (workStatus is not ("Working" or "RequestedFromProvider"))
            throw new ArgumentException("Estado de trabajo inválido.", nameof(workStatus));
        WorkStatus = workStatus;
        WorkStatusUpdatedAtUtc = updatedAtUtc;
    }

    public void MarkSlaReminderSent(DateTime sentAtUtc)
    {
        SlaReminderSentAtUtc = sentAtUtc;
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

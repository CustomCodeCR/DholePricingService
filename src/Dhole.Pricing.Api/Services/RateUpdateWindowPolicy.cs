using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.Api.Services;

public sealed record RateUpdateWindowDecision(
    bool CanUpdate,
    string? UpdateWindow,
    string Message
);

public static class RateUpdateWindowPolicy
{
    public static RateUpdateWindowDecision Evaluate(
        RateStatus status,
        bool hasLinkedRequest,
        bool requestStillOpen)
    {
        if (status is RateStatus.AcceptedByClient
            or RateStatus.RejectedByClient
            or RateStatus.Closed
            or RateStatus.Expired)
        {
            return new RateUpdateWindowDecision(false, null,
                "La tarifa ya no puede actualizarse porque el vendedor ya tomó una decisión o la tarifa está cerrada/vencida.");
        }

        var beforePricingSends = hasLinkedRequest
            && requestStillOpen
            && status is RateStatus.PendingApproval
                or RateStatus.ApprovedByManagement
                or RateStatus.RejectedByManagement
                or RateStatus.Open;

        if (beforePricingSends)
        {
            return new RateUpdateWindowDecision(true, "BeforePricingSend",
                "Puede actualizar la misma solicitud antes de que el operativo de Pricing la envíe. Debe indicar el motivo de la actualización.");
        }

        if (status == RateStatus.Sent)
        {
            return new RateUpdateWindowDecision(true, "WaitingSellerDecision",
                "La tarifa está enviada y puede actualizarse mientras el vendedor no la acepte ni la rechace. Debe indicar el motivo de la actualización.");
        }

        return new RateUpdateWindowDecision(false, null,
            "La tarifa solo puede actualizarse dentro de la misma solicitud antes del envío de Pricing o cuando está Enviada y pendiente de aceptación/rechazo del vendedor.");
    }
}

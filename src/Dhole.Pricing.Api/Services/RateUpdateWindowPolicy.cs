using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.Api.Services;

public sealed record RateUpdateWindowDecision(
    bool CanUpdate,
    string? UpdateWindow,
    string Message
);

/// <summary>
/// The quote edit window must be consistent with UpdateRateCommandHandler:
/// editing a commercial quote creates a new revision (including an accepted
/// or expired quote); a finalized Closed quote remains immutable.
/// An unrelated completed seller request must not disable manual editing.
/// </summary>
public static class RateUpdateWindowPolicy
{
    public static RateUpdateWindowDecision Evaluate(
        RateStatus status,
        bool hasLinkedRequest,
        bool requestStillOpen)
    {
        if (status == RateStatus.Closed)
            return new(false, null,
                "La tarifa está cerrada y no se puede modificar. Cree una nueva cotización si necesita cambiarla.");

        if (hasLinkedRequest && requestStillOpen)
            return new(true, "LinkedRequestOpen",
                "Puede editar la cotización vinculada a una solicitud abierta. Indique el motivo del cambio.");

        if (status is RateStatus.AcceptedByClient or RateStatus.RejectedByClient or RateStatus.Expired)
            return new(true, "NewRevision",
                "Puede crear una nueva revisión sin alterar el historial comercial anterior. Indique el motivo del cambio.");

        if (status is RateStatus.Sent or RateStatus.RequestedByClient)
            return new(true, "CommercialRevision",
                "Puede modificar la tarifa y generar una revisión nueva pendiente de envío. Indique el motivo del cambio.");

        return new(true, "EditableQuotation",
            "Puede guardar modificaciones a la tarifa y se conservará el historial de revisiones. Indique el motivo del cambio.");
    }
}

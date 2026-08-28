from pathlib import Path

path = Path('src/Dhole.Pricing.Domain/Rates/Entities/RateHeader.cs')
text = path.read_text(encoding='utf-8')

old = '''            (RateStatus.ApprovedByManagement, RateStatus.Open) => true,
            (RateStatus.Open, RateStatus.Sent) => true,'''
new = '''            (RateStatus.ApprovedByManagement, RateStatus.Open) => true,
            // Una solicitud puede guardarse antes de tener proveedor/costos terminados.
            // RequestedByClient funciona como la cola interna de "Abiertas" de Pricing.
            (RateStatus.PendingApproval, RateStatus.RequestedByClient) => true,
            (RateStatus.Open, RateStatus.Sent) => true,'''
if old not in text:
    raise SystemExit('transition anchor not found')
text = text.replace(old, new, 1)

old = '''        if (isClosing && string.IsNullOrWhiteSpace(reason))
        {
            throw new InvalidOperationException("El motivo de cierre es obligatorio.");
        }

        if (isClosing && reason!.Trim().Length > 1000)'''
new = '''        if (status == RateStatus.AcceptedByClient && string.IsNullOrWhiteSpace(IdtraNumber))
        {
            throw new InvalidOperationException("Para aceptar la tarifa debe registrar el IDTRA.");
        }

        if (status == RateStatus.RejectedByClient && string.IsNullOrWhiteSpace(reason))
        {
            throw new InvalidOperationException("El motivo de no aceptación del cliente es obligatorio.");
        }

        if (isClosing && string.IsNullOrWhiteSpace(reason))
        {
            throw new InvalidOperationException("El motivo de cierre es obligatorio.");
        }

        if ((isClosing || status == RateStatus.RejectedByClient) && reason!.Trim().Length > 1000)'''
if old not in text:
    raise SystemExit('reason anchor not found')
text = text.replace(old, new, 1)

old = '''        if (isClosing)
        {
            ClosedReason = reason!.Trim();
            ClosedAtUtc = DateTime.UtcNow;
            ClosedBy = updatedBy;
        }
'''
new = '''        if (isClosing || status == RateStatus.RejectedByClient)
        {
            ClosedReason = reason?.Trim();
            ClosedAtUtc = DateTime.UtcNow;
            ClosedBy = updatedBy;
        }
'''
if old not in text:
    raise SystemExit('closed metadata anchor not found')
text = text.replace(old, new, 1)

path.write_text(text, encoding='utf-8')

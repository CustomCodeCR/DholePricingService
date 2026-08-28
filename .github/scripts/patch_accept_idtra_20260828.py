from pathlib import Path

# Endpoint forwards IDTRA in the same acceptance request.
path = Path('src/Dhole.Pricing.Api/Endpoints/RateEndpoints.cs')
text = path.read_text(encoding='utf-8')
old = '''            new SetRateStatusCommand(
                rateId,
                status,
                request.Reason,
                httpContext.GetCurrentUserId()
            ),
'''
new = '''            new SetRateStatusCommand(
                rateId,
                status,
                request.Reason,
                request.IdtraNumber,
                httpContext.GetCurrentUserId()
            ),
'''
if old not in text:
    raise SystemExit('RateEndpoints SetRateStatusCommand anchor not found')
path.write_text(text.replace(old, new, 1), encoding='utf-8')

# Handler persists supplied IDTRA before validating AcceptedByClient transition.
path = Path('src/Dhole.Pricing.Application/Features/Rates/SetRateStatus/SetRateStatusCommandHandler.cs')
text = path.read_text(encoding='utf-8')
old = '''        try
        {
            rate.SetCommercialStatus(command.Status, command.Reason, command.UpdatedBy);
        }
'''
new = '''        try
        {
            if (command.Status == Dhole.Pricing.Domain.Rates.Enums.RateStatus.AcceptedByClient
                && !string.IsNullOrWhiteSpace(command.IdtraNumber))
            {
                rate.SetIdtraNumber(command.IdtraNumber, command.UpdatedBy);
            }

            rate.SetCommercialStatus(command.Status, command.Reason, command.UpdatedBy);
        }
'''
if old not in text:
    raise SystemExit('SetRateStatus handler anchor not found')
text = text.replace(old, new, 1)
old = '''                    rate.ClosedReason,
                    rate.ClosedAtUtc,
                    rate.ClosedBy,
'''
new = '''                    rate.IdtraNumber,
                    rate.ClosedReason,
                    rate.ClosedAtUtc,
                    rate.ClosedBy,
'''
if old not in text:
    raise SystemExit('SetRateStatus audit anchor not found')
path.write_text(text.replace(old, new, 1), encoding='utf-8')

# Domain method centralizes IDTRA validation and persistence.
path = Path('src/Dhole.Pricing.Domain/Rates/Entities/RateHeader.cs')
text = path.read_text(encoding='utf-8')
anchor = '''    public void SetCommercialStatus(RateStatus status, string? reason, Guid? updatedBy)
    {
'''
insert = '''    public void SetIdtraNumber(string idtraNumber, Guid? updatedBy)
    {
        var normalized = Normalize(idtraNumber);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("El IDTRA es obligatorio.");
        }

        if (normalized.Length > 100)
        {
            throw new InvalidOperationException("El IDTRA no puede superar los 100 caracteres.");
        }

        IdtraNumber = normalized;
        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());
        AddDomainEvent(new RateHeaderUpdatedDomainEvent(Id, updatedBy));
    }

'''
if anchor not in text:
    raise SystemExit('RateHeader SetCommercialStatus anchor not found')
path.write_text(text.replace(anchor, insert + anchor, 1), encoding='utf-8')

print('Client acceptance IDTRA patch applied')

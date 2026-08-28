from pathlib import Path

handler = Path('src/Dhole.Pricing.Application/Features/Rates/CreateRate/CreateRateCommandHandler.cs')
text = handler.read_text()
old = """        var rateCode = await rateCodeGenerator.GenerateAsync(cancellationToken);

        RateHeader rate;"""
new = """        var rateCode = await rateCodeGenerator.GenerateAsync(cancellationToken);

        // Toda solicitud/tarifa nace con un QUO de seguimiento. RateCode ya utiliza
        // el consecutivo QUO del servicio; QuoNumber lo conserva como dato comercial explícito.
        if (string.IsNullOrWhiteSpace(command.QuoNumber))
        {
            command = command with { QuoNumber = rateCode };
        }

        RateHeader rate;"""
if old not in text:
    raise SystemExit('CreateRate QUO insertion point not found')
text = text.replace(old, new, 1)
handler.write_text(text)

# Validate the business contract that already lives in domain/repository/worker.
domain = Path('src/Dhole.Pricing.Domain/Rates/Entities/RateHeader.cs').read_text()
for marker in (
    'status == RateStatus.AcceptedByClient && string.IsNullOrWhiteSpace(IdtraNumber)',
    'status == RateStatus.RejectedByClient && string.IsNullOrWhiteSpace(reason)',
    '(RateStatus.Open, RateStatus.Sent) => true',
    '(RateStatus.Sent, RateStatus.AcceptedByClient) => true',
    '(RateStatus.Sent, RateStatus.RejectedByClient) => true',
):
    if marker not in domain:
        raise SystemExit(f'Missing domain business rule: {marker}')

worker = Path('src/Dhole.Pricing.Workers/Workers/PricingRateExpirationWorker.cs').read_text()
if 'rate.Status == RateStatus.Sent' not in worker or 'rate.ValidTo < todayUtc' not in worker:
    raise SystemExit('Expiration must apply only to sent rates after validity')

repository = Path('src/Dhole.Pricing.Persistence/Repositories/RateHeaderRepository.cs').read_text()
for marker in (
    'RateStatus.Open => query.Where(x =>',
    'x.Status == RateStatus.PendingApproval',
    'x.Status == RateStatus.RequestedByClient',
    'RateStatus.RejectedByClient => query.Where(x =>',
    'x.Status == RateStatus.RejectedByClient || x.Status == RateStatus.Closed',
):
    if marker not in repository:
        raise SystemExit(f'Missing commercial browse grouping: {marker}')

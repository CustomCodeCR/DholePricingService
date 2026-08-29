from pathlib import Path

ROOT = Path('.')
def read(path): return (ROOT/path).read_text(encoding='utf-8')
def write(path, content): (ROOT/path).write_text(content, encoding='utf-8')
def replace(path, old, new, count=1):
    text = read(path)
    found = text.count(old)
    if found != count:
        raise RuntimeError(f'{path}: expected {count} matches, found {found}: {old[:160]!r}')
    write(path, text.replace(old, new, count))

# SetAmounts must not re-accept a new revision just because the previous accepted revision
# already had IDTRA + QUO. Client acceptance remains an explicit commercial transition.
replace(
    'src/Dhole.Pricing.Domain/Rates/Entities/RateHeader.cs',
    '''        if (IdtraNumber is not null && QuoNumber is not null)\n        {\n            RequiredApproval = false;\n            Status = RateStatus.AcceptedByClient;\n        }\n        else if (MarginPercentage >= MinimumMarginPercentage)''',
    '''        if (Status == RateStatus.AcceptedByClient)\n        {\n            // Recalcular una revisión aceptada conserva su estado únicamente durante la\n            // mutación actual. UpdateRateCommandHandler la convierte después en una nueva\n            // revisión Open/PendingApproval, preservando primero la versión aceptada.\n            RequiredApproval = false;\n        }\n        else if (MarginPercentage >= MinimumMarginPercentage)'''
)

# The update contract also carries the exchange rate edited in the same wizard.
replace(
    'src/Dhole.Pricing.Contracts/Rates/Request/UpdateRateRequest.cs',
    '''    string OperationType = "TransitDomestic",\n    IReadOnlyCollection<RateServiceRequest>? Services = null\n);''',
    '''    string OperationType = "TransitDomestic",\n    IReadOnlyCollection<RateServiceRequest>? Services = null,\n    decimal? ExchangeRatePurchase = null,\n    decimal? ExchangeRateSale = null,\n    decimal? ExchangeRateApplied = null\n);'''
)

replace(
    'src/Dhole.Pricing.Application/Features/Rates/UpdateRate/UpdateRateCommand.cs',
    '''    RateOperationType OperationType,\n    IReadOnlyCollection<RateServiceSelection> Services,\n    Guid? UpdatedBy''',
    '''    RateOperationType OperationType,\n    IReadOnlyCollection<RateServiceSelection> Services,\n    decimal? ExchangeRatePurchase,\n    decimal? ExchangeRateSale,\n    decimal? ExchangeRateApplied,\n    Guid? UpdatedBy'''
)

replace(
    'src/Dhole.Pricing.Api/Endpoints/RateEndpoints.cs',
    '''                (request.Services ?? [])\n                    .Select(x => new RateServiceSelection(x.Id, x.Name, x.Code))\n                    .ToArray(),\n                httpContext.GetCurrentUserId()\n            ),''',
    '''                (request.Services ?? [])\n                    .Select(x => new RateServiceSelection(x.Id, x.Name, x.Code))\n                    .ToArray(),\n                request.ExchangeRatePurchase,\n                request.ExchangeRateSale,\n                request.ExchangeRateApplied,\n                httpContext.GetCurrentUserId()\n            ),''',
    count=1,
)

# Apply the exchange rate snapshot before totals are recalculated. If it did not change,
# keep its original source/date semantics; if it changed, identify it as a wizard override.
replace(
    'src/Dhole.Pricing.Application/Features/Rates/UpdateRate/UpdateRateCommandHandler.cs',
    '''            rate.ConfigurePickupLocation(\n                command.PickupAddress,\n                command.PickupLatitude,\n                command.PickupLongitude\n            );\n\n            rate.ReplaceContainerAllocations(containerSpecs, command.UpdatedBy);''',
    '''            rate.ConfigurePickupLocation(\n                command.PickupAddress,\n                command.PickupLatitude,\n                command.PickupLongitude\n            );\n\n            if (command.ExchangeRateApplied is > 0m || command.ExchangeRateSale is > 0m)\n            {\n                var appliedRate = command.ExchangeRateApplied is > 0m\n                    ? command.ExchangeRateApplied.Value\n                    : command.ExchangeRateSale!.Value;\n                var purchaseRate = command.ExchangeRatePurchase is > 0m\n                    ? command.ExchangeRatePurchase\n                    : rate.ExchangeRatePurchase;\n                var saleRate = command.ExchangeRateSale is > 0m\n                    ? command.ExchangeRateSale\n                    : rate.ExchangeRateSale;\n                var exchangeChanged =\n                    purchaseRate != rate.ExchangeRatePurchase\n                    || saleRate != rate.ExchangeRateSale\n                    || appliedRate != rate.ExchangeRateApplied;\n\n                rate.ConfigureExchangeRateSnapshot(\n                    purchaseRate,\n                    saleRate,\n                    appliedRate,\n                    exchangeChanged ? DateTime.UtcNow.Date : rate.ExchangeRateDate,\n                    DateTime.UtcNow,\n                    exchangeChanged ? "Wizard Pricing · ajuste manual" : rate.ExchangeRateSource ?? "Wizard Pricing",\n                    exchangeChanged || rate.ExchangeRateManualOverride,\n                    command.UpdatedBy\n                );\n            }\n\n            rate.ReplaceContainerAllocations(containerSpecs, command.UpdatedBy);'''
)

print('Revision state and wizard exchange-rate behavior applied.')

from pathlib import Path


def replace_once(path: str, old: str, new: str):
    p = Path(path)
    text = p.read_text()
    if old not in text:
        raise SystemExit(f"pattern not found in {path}: {old[:180]!r}")
    p.write_text(text.replace(old, new, 1))

# Request: persist the two visible/editable exchange rates.
replace_once(
    "src/Dhole.Pricing.Contracts/Rates/Request/CreateRateRequest.cs",
    "    decimal? PickupLongitude = null,\n    decimal? ExchangeRateApplied = null\n);",
    "    decimal? PickupLongitude = null,\n    decimal? ExchangeRatePurchase = null,\n    decimal? ExchangeRateSale = null,\n    decimal? ExchangeRateApplied = null\n);",
)

# Command: carry purchase and sale independently through the application layer.
replace_once(
    "src/Dhole.Pricing.Application/Features/Rates/CreateRate/CreateRateCommand.cs",
    "    decimal? PickupLongitude,\n    decimal? ExchangeRateApplied,\n    bool CanApproveImportedRate,",
    "    decimal? PickupLongitude,\n    decimal? ExchangeRatePurchase,\n    decimal? ExchangeRateSale,\n    decimal? ExchangeRateApplied,\n    bool CanApproveImportedRate,",
)

# API mapping.
replace_once(
    "src/Dhole.Pricing.Api/Endpoints/RateEndpoints.cs",
    "                request.PickupLongitude,\n                request.ExchangeRateApplied,\n                canApproveImportedRate,",
    "                request.PickupLongitude,\n                request.ExchangeRatePurchase,\n                request.ExchangeRateSale,\n                request.ExchangeRateApplied,\n                canApproveImportedRate,",
)

# Creation rules: Hacienda remains the automatic default, but the two visible values
# can be overridden independently. If Hacienda is unavailable, both manual values are enough.
replace_once(
    "src/Dhole.Pricing.Application/Features/Rates/CreateRate/CreateRateCommandHandler.cs",
    "        var officialExchangeRate = await exchangeRateProvider.GetUsdCrcAsync(cancellationToken);\n        var requestedAppliedExchangeRate = command.ExchangeRateApplied is > 0m\n            ? command.ExchangeRateApplied.Value\n            : officialExchangeRate?.Sale;\n        if (requestedAppliedExchangeRate is null or <= 0m)\n            return Result.Failure<Guid>(PricingErrors.ExchangeRateUnavailable);",
    "        var officialExchangeRate = await exchangeRateProvider.GetUsdCrcAsync(cancellationToken);\n        var resolvedExchangeRatePurchase = command.ExchangeRatePurchase is > 0m\n            ? command.ExchangeRatePurchase.Value\n            : officialExchangeRate?.Purchase;\n        var resolvedExchangeRateSale = command.ExchangeRateSale is > 0m\n            ? command.ExchangeRateSale.Value\n            : officialExchangeRate?.Sale;\n\n        if (resolvedExchangeRatePurchase is null or <= 0m || resolvedExchangeRateSale is null or <= 0m)\n            return Result.Failure<Guid>(PricingErrors.ExchangeRateUnavailable);\n\n        // ExchangeRateApplied is kept only for backwards compatibility. New screens use\n        // Compra/Venta directly and internally the sale rate remains the conversion default.\n        var requestedAppliedExchangeRate = command.ExchangeRateApplied is > 0m\n            ? command.ExchangeRateApplied.Value\n            : resolvedExchangeRateSale.Value;\n\n        var exchangeRateWasAdjustedManually = officialExchangeRate is null\n            || Math.Abs(resolvedExchangeRatePurchase.Value - officialExchangeRate.Purchase) > 0.0001m\n            || Math.Abs(resolvedExchangeRateSale.Value - officialExchangeRate.Sale) > 0.0001m;\n\n        var exchangeRateSource = officialExchangeRate is null\n            ? \"Manual\"\n            : exchangeRateWasAdjustedManually\n                ? $\"{officialExchangeRate.Source} (ajustado manualmente)\"\n                : officialExchangeRate.Source;",
)

replace_once(
    "src/Dhole.Pricing.Application/Features/Rates/CreateRate/CreateRateCommandHandler.cs",
    "            rate.ConfigureExchangeRateSnapshot(\n                officialExchangeRate?.Purchase,\n                officialExchangeRate?.Sale,\n                requestedAppliedExchangeRate.Value,\n                officialExchangeRate?.RateDate,\n                officialExchangeRate?.CapturedAtUtc ?? DateTime.UtcNow,\n                officialExchangeRate?.Source ?? \"Manual (Hacienda no disponible al crear)\",\n                command.CreatedBy\n            );",
    "            rate.ConfigureExchangeRateSnapshot(\n                resolvedExchangeRatePurchase.Value,\n                resolvedExchangeRateSale.Value,\n                requestedAppliedExchangeRate,\n                officialExchangeRate?.RateDate,\n                officialExchangeRate?.CapturedAtUtc ?? DateTime.UtcNow,\n                exchangeRateSource,\n                exchangeRateWasAdjustedManually,\n                command.CreatedBy\n            );",
)

# Domain snapshot explicitly records whether Compra/Venta were adjusted.
replace_once(
    "src/Dhole.Pricing.Domain/Rates/Entities/RateHeader.cs",
    "        DateTime capturedAtUtc,\n        string source,\n        Guid? updatedBy\n    )",
    "        DateTime capturedAtUtc,\n        string source,\n        bool manualOverride,\n        Guid? updatedBy\n    )",
)
replace_once(
    "src/Dhole.Pricing.Domain/Rates/Entities/RateHeader.cs",
    "        ExchangeRateSource = Normalize(source) ?? \"Manual\";\n        ExchangeRateManualOverride = !sale.HasValue || Math.Abs(applied - sale.Value) > 0.0001m;",
    "        ExchangeRateSource = Normalize(source) ?? \"Manual\";\n        ExchangeRateManualOverride = manualOverride;",
)

print("Editable Hacienda purchase/sale backend patch applied")

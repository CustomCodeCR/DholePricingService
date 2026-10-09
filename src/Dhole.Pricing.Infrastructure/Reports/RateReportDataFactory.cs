using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dhole.Pricing.Application.Abstractions.Reports;
using Dhole.Pricing.Contracts.Rates.Response;
using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;
using Microsoft.Extensions.Configuration;
using QRCoder;

namespace Dhole.Pricing.Infrastructure.Reports;

public sealed class RateReportDataFactory(IConfiguration configuration) : IRateReportDataFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly CultureInfo MoneyCulture = CultureInfo.GetCultureInfo("en-US");
    private const string OriginOfficeMessage = "Estos son los datos de Castro Fallas en origen.";
    private const decimal MiamiCftPerCbm = 35.3146667m;
    private const decimal MiamiKgPerCft = 14.16m;

    public string CreateDataJson(RateHeader rate)
    {
        string Text(string? value, string fallback = "No especificado") =>
            string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        string CurrencyValue(string? name, string? code) =>
            Text(name, Text(code, "USD"));
        string CurrencyGroupKey(RateDetail detail) =>
            CurrencyValue(detail.CurrencyName, detail.CurrencyCode).Trim().ToUpperInvariant();
        string DetailMoney(RateDetail detail, decimal amount) =>
            $"{CurrencyValue(detail.CurrencyName, detail.CurrencyCode)} {amount.ToString("N2", MoneyCulture)}";
        string CurrencyIso(string? code, string? name)
        {
            var normalizedCode = (code ?? string.Empty).Trim().ToUpperInvariant();
            var normalizedName = (name ?? string.Empty).Trim().ToUpperInvariant();
            if (normalizedCode is "USD" or "CRC") return normalizedCode;
            if (normalizedName.Contains("USD", StringComparison.Ordinal)) return "USD";
            if (normalizedName.Contains("CRC", StringComparison.Ordinal)
                || normalizedName.Contains("COLON", StringComparison.Ordinal)
                || normalizedName.Contains("COLÓN", StringComparison.Ordinal)) return "CRC";
            return normalizedCode;
        }
        bool IsPickupCharge(RateDetail detail) =>
            detail.Name.Trim().StartsWith("Recolecta", StringComparison.OrdinalIgnoreCase)
            || detail.Name.Trim().StartsWith("Recolección", StringComparison.OrdinalIgnoreCase)
            || detail.Name.Trim().StartsWith("Pickup", StringComparison.OrdinalIgnoreCase)
            || detail.Name.Trim().StartsWith("Pick up", StringComparison.OrdinalIgnoreCase);

        var currencyValue = CurrencyValue(rate.CurrencyName, rate.CurrencyCode);
        var commercialTerms = ExclusiveCommercialTerms(rate.Includes, rate.SubjectTo, rate.Excludes);
        var originOfficePublicUrl = CreateOriginOfficePublicUrl(rate);
        var originOfficeQrDataUri = CreateQrDataUri(originOfficePublicUrl);
        var isLand = rate.ShipmentMode is ShipmentMode.Ftl or ShipmentMode.Ltl;
        var showAgent = true;
        var reportAgentName = isLand ? "Grupo Castro Fallas" : Text(rate.AgentName, "No asignado");
        var showCarrier = rate.ShipmentMode == ShipmentMode.Fcl;
        var route = !string.IsNullOrWhiteSpace(rate.PodName)
            ? $"{rate.PolName} → {rate.PodName} vía {rate.PoeName}"
            : $"{rate.PolName} → {rate.PoeName}";
        var cargoDetails = CreateCargoDetails(rate.CargoLinesJson);
        var pickupLocations = CreatePickupLocations(
            rate.PickupLocationsJson, rate.PickupAddress, rate.IncotermCode, rate.IncotermName);

        // Reconcile only previously persisted Miami CFT freight corrupted by
        // the old 1-CBM floor. Modern, correctly saved CFT lines are unchanged.
        var miamiBillableCft = CalculateMiamiBillableCft(rate);
        decimal ReportQuantity(RateDetail detail)
        {
            // Recolecta is a flat fee per concept, even on historical rows persisted as CFT/CBM.
            if (IsPickupCharge(detail))
                return 1m;

            if (!miamiBillableCft.HasValue
                || detail.CostDetailType != CostDetailType.Freight
                || detail.ChargeBasis != ChargeBasis.PerChargeableCft)
                return detail.Quantity;

            var legacyCftFromCbmFloor = Math.Max(rate.ChargeableQuantity, 1m) * MiamiCftPerCbm;
            var legacyFloorApplied =
                Math.Abs(detail.Quantity - legacyCftFromCbmFloor) < 0.05m
                && miamiBillableCft.Value + 0.05m < detail.Quantity;

            return legacyFloorApplied ? miamiBillableCft.Value : detail.Quantity;
        }

        var cftFreight = rate.ShipmentMode == ShipmentMode.Lcl
            ? rate.RateDetails.FirstOrDefault(detail =>
                detail.CostDetailType == CostDetailType.Freight &&
                detail.ChargeBasis == ChargeBasis.PerChargeableCft)
            : null;
        // The quote header and its freight line must use the same commercial unit.
        var lclChargeableLabel = cftFreight is not null
            ? $"LCL · {ReportQuantity(cftFreight).ToString("N3", MoneyCulture)} CFT cobrable"
            : $"LCL · {rate.ChargeableQuantity.ToString("N3", MoneyCulture)} CBM cobrable";

        // LCL must never leak the legacy container placeholder (for example 20 DV)
        // into the commercial document. For consolidated cargo the shipment itself is
        // the equipment row and its real commercial measure is the chargeable CBM.
        var containers = rate.ShipmentMode == ShipmentMode.Lcl
            ? new[]
            {
                new
                {
                    containerTypeId = rate.ContainerTypeId,
                    containerType = "LCL",
                    containerTypeName = lclChargeableLabel,
                    containerTypeCode = "LCL",
                    quantity = 1,
                    label = lclChargeableLabel
                }
            }
            : (rate.RateContainers.Count > 0
                    ? rate.RateContainers
                        .OrderBy(x => x.ContainerTypeName)
                        .ThenBy(x => x.ContainerTypeCode)
                        .Select(x => new
                        {
                            containerTypeId = x.ContainerTypeId,
                            containerType = rate.ShipmentMode == ShipmentMode.Ftl
                                ? CompactLandEquipmentLabel(x.ContainerTypeName, x.ContainerTypeCode)
                                : x.ContainerTypeName,
                            containerTypeName = rate.ShipmentMode == ShipmentMode.Ftl
                                ? CompactLandEquipmentLabel(x.ContainerTypeName, x.ContainerTypeCode)
                                : x.ContainerTypeName,
                            containerTypeCode = x.ContainerTypeCode,
                            quantity = x.Quantity,
                            label = rate.ShipmentMode == ShipmentMode.Ftl
                                ? $"{x.Quantity}x{CompactLandEquipmentLabel(x.ContainerTypeName, x.ContainerTypeCode)}"
                                : $"{x.Quantity} x {x.ContainerTypeName}"
                        })
                    : new[]
                    {
                        new
                        {
                            containerTypeId = rate.ContainerTypeId,
                            containerType = rate.ShipmentMode == ShipmentMode.Ftl
                                ? CompactLandEquipmentLabel(rate.ContainerTypeName, rate.ContainerTypeCode)
                                : rate.ContainerTypeName,
                            containerTypeName = rate.ShipmentMode == ShipmentMode.Ftl
                                ? CompactLandEquipmentLabel(rate.ContainerTypeName, rate.ContainerTypeCode)
                                : rate.ContainerTypeName,
                            containerTypeCode = rate.ContainerTypeCode,
                            quantity = rate.ContainerQuantity,
                            label = rate.ShipmentMode == ShipmentMode.Ftl
                                ? $"{rate.ContainerQuantity}x{CompactLandEquipmentLabel(rate.ContainerTypeName, rate.ContainerTypeCode)}"
                                : $"{rate.ContainerQuantity} x {rate.ContainerTypeName}"
                        }
                    })
                .ToArray();
        var equipmentSummary = string.Join(" + ", containers.Select(x => x.label));
        var shipmentSummary = rate.ShipmentMode switch
        {
            ShipmentMode.Lcl => lclChargeableLabel,
            ShipmentMode.Ltl => $"LTL · {rate.ChargeableQuantity.ToString("N3", MoneyCulture)} CBM cobrable",
            ShipmentMode.Ftl => equipmentSummary,
            _ => equipmentSummary,
        };

        // El PDF refleja los detalles comerciales persistidos (seleccionados en Pricing).
        // CostId identifica un costo del catálogo, no una instrucción para ocultarlo.
        // Filtrarlo para LCL propio eliminaba recolectas y otros cargos realmente cotizados.
        // La limpieza de costos de otro proveedor debe realizarse al cambiar la fuente
        // y guardar la tarifa, nunca silenciosamente al generar el documento.
        var reportDetails = rate.RateDetails
            // Una recolecta seleccionada debe figurar aunque su precio siga en cero;
            // no inventamos importes ni mostramos otros conceptos automáticos sin precio.
            .Where(detail => IsPickupCharge(detail) || detail.SaleAmount * ReportQuantity(detail) != 0m)
            .OrderBy(x => x.CostDetailType)
            .ThenBy(x => x.Name)
            .ToArray();

        var useAllInPresentation = rate.UseAllInPresentation && reportDetails.Length > 0;
        var allInAmount = useAllInPresentation
            ? CalculateAllInAmount(rate, reportDetails, ReportQuantity)
            : 0m;
        var allInIncludesDestinationTax = reportDetails.Any(detail =>
            detail.ApplyDestinationTax && detail.DestinationTaxRate > 0m);

        var items = useAllInPresentation
            ? new[]
            {
                new
                {
                    description = "ALL IN",
                    quantity = 1m,
                    currency = currencyValue,
                    currencyCode = rate.CurrencyCode,
                    unitSale = $"{currencyValue} {allInAmount.ToString("N2", MoneyCulture)}",
                    unitSaleAmount = allInAmount,
                    lineTotal = $"{currencyValue} {allInAmount.ToString("N2", MoneyCulture)}",
                    lineTotalAmount = allInAmount,
                    destinationTaxLabel = allInIncludesDestinationTax ? "IVA incluido" : string.Empty,
                    notes = string.Empty
                }
            }
            : reportDetails
                .Select(detail => new
                {
                    description = detail.Name,
                    quantity = ReportQuantity(detail),
                    currency = CurrencyValue(detail.CurrencyName, detail.CurrencyCode),
                    currencyCode = detail.CurrencyCode,
                    unitSale = DetailMoney(detail, detail.SaleAmount),
                    unitSaleAmount = detail.SaleAmount,
                    lineTotal = DetailMoney(detail, detail.SaleAmount * ReportQuantity(detail)),
                    lineTotalAmount = detail.SaleAmount * ReportQuantity(detail),
                    destinationTaxLabel = detail.ApplyDestinationTax && detail.DestinationTaxRate > 0m
                        ? "IVA incluido"
                        : string.Empty,
                    notes = detail.CostDetailType == CostDetailType.Insurance
                        ? string.Empty
                        : Text(detail.Notes, string.Empty)
                })
                .ToArray();

        // Agrupar por la moneda que realmente se muestra al cliente. Algunos registros
        // históricos tienen CurrencyCode distintos/legacy aunque CurrencyName sea el mismo
        // (por ejemplo USD), lo que antes generaba dos tarjetas USD en el mismo PDF.
        var originalCurrencyTotals = useAllInPresentation
            ? new[]
            {
                new
                {
                    currency = currencyValue,
                    currencyCode = Text(rate.CurrencyCode, string.Empty),
                    amount = allInAmount,
                    total = $"{currencyValue} {allInAmount.ToString("N2", MoneyCulture)}"
                }
            }
            : reportDetails
                .GroupBy(CurrencyGroupKey, StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var first = group.First();
                    var displayCurrency = CurrencyValue(first.CurrencyName, first.CurrencyCode);
                    var amount = group.Sum(detail => detail.SaleAmount * ReportQuantity(detail));
                    var canonicalCode = Regex.IsMatch(group.Key, "^[A-Z]{3}$")
                        ? group.Key
                        : Text(first.CurrencyCode, string.Empty);

                    return new
                    {
                        currency = displayCurrency,
                        currencyCode = canonicalCode,
                        amount,
                        total = $"{displayCurrency} {amount.ToString("N2", MoneyCulture)}"
                    };
                })
                .OrderBy(x => x.currencyCode)
                .ThenBy(x => x.currency)
                .ToArray();

        // Use the exchange-rate snapshot saved with the quote, never an external/current FX rate.
        // Convert the same normalized detail quantities that the PDF prints, so both cards
        // represent ONE payable total rather than two independent subtotals.
        var appliedExchangeRate = rate.ExchangeRateApplied is > 0m
            ? rate.ExchangeRateApplied
            : rate.ExchangeRateSale;
        var canShowEquivalentTotals = appliedExchangeRate is > 0m
            && (useAllInPresentation
                ? CurrencyIso(rate.CurrencyCode, rate.CurrencyName) is "USD" or "CRC"
                : reportDetails.All(detail =>
                    CurrencyIso(detail.CurrencyCode, detail.CurrencyName) is "USD" or "CRC"));
        var nativeUsd = useAllInPresentation
            ? (CurrencyIso(rate.CurrencyCode, rate.CurrencyName) == "USD" ? allInAmount : 0m)
            : reportDetails.Where(detail => CurrencyIso(detail.CurrencyCode, detail.CurrencyName) == "USD")
                .Sum(detail => detail.SaleAmount * ReportQuantity(detail));
        var nativeCrc = useAllInPresentation
            ? (CurrencyIso(rate.CurrencyCode, rate.CurrencyName) == "CRC" ? allInAmount : 0m)
            : reportDetails.Where(detail => CurrencyIso(detail.CurrencyCode, detail.CurrencyName) == "CRC")
                .Sum(detail => detail.SaleAmount * ReportQuantity(detail));
        var equivalentUsd = canShowEquivalentTotals
            ? decimal.Round(nativeUsd + nativeCrc / appliedExchangeRate!.Value, 2, MidpointRounding.AwayFromZero)
            : 0m;
        var equivalentCrc = canShowEquivalentTotals
            ? decimal.Round(nativeCrc + nativeUsd * appliedExchangeRate!.Value, 2, MidpointRounding.AwayFromZero)
            : 0m;
        var currencyTotals = canShowEquivalentTotals
            ? new[]
            {
                new { currency = "USD", currencyCode = "USD", amount = equivalentUsd,
                    total = $"USD {equivalentUsd.ToString("N2", MoneyCulture)}" },
                new { currency = "CRC", currencyCode = "CRC", amount = equivalentCrc,
                    total = $"CRC {equivalentCrc.ToString("N2", MoneyCulture)}" }
            }
            : originalCurrencyTotals;
        var hasSingleCurrency = currencyTotals.Length == 1;
        var hasMultipleCurrencies = currencyTotals.Length > 1;
        var hasSeparateCurrencyTotals = hasMultipleCurrencies && !canShowEquivalentTotals;
        var exchangeRateNote = canShowEquivalentTotals
            ? $"Tipo de cambio aplicado: CRC {appliedExchangeRate!.Value.ToString("N4", MoneyCulture)} por USD 1.00"
                + (rate.ExchangeRateDate.HasValue ? $" · {rate.ExchangeRateDate.Value:dd/MM/yyyy}" : string.Empty)
            : string.Empty;
        var reportTotal = hasSingleCurrency
            ? currencyTotals[0].total
            : hasMultipleCurrencies
                ? (canShowEquivalentTotals ? "Total equivalente en USD y CRC" : "Totales por moneda")
                : $"{currencyValue} 0.00";
        var reportTotalAmount = hasSingleCurrency ? currencyTotals[0].amount : 0m;

        var rows = useAllInPresentation
            ? new[]
            {
                new Dictionary<string, object?>
                {
                    ["Concepto"] = "ALL IN",
                    ["Cantidad"] = 1m,
                    ["Moneda"] = currencyValue,
                    ["Precio unitario"] = allInAmount,
                    ["Total"] = allInAmount,
                    ["Notas"] = string.Empty
                }
            }
            : reportDetails
                .Select(detail => new Dictionary<string, object?>
                {
                    ["Concepto"] = detail.Name,
                    ["Cantidad"] = ReportQuantity(detail),
                    ["Moneda"] = CurrencyValue(detail.CurrencyName, detail.CurrencyCode),
                    ["Precio unitario"] = detail.SaleAmount,
                    ["Total"] = detail.SaleAmount * ReportQuantity(detail),
                    ["Notas"] = detail.CostDetailType == CostDetailType.Insurance
                        ? string.Empty
                        : detail.Notes
                })
                .ToArray();

        var data = new
        {
            company = new
            {
                name = configuration["Reports:Company:Name"] ?? "Grupo Castro Fallas",
                legalName = configuration["Reports:Company:LegalName"] ?? "Grupo Castro Fallas",
                phone = configuration["Reports:Company:Phone"] ?? string.Empty,
                email = configuration["Reports:Company:Email"] ?? string.Empty,
                website = configuration["Reports:Company:Website"] ?? "https://logisticacastrofallas.com",
                logoDataUri = configuration["Reports:Company:LogoDataUri"] ?? string.Empty
            },
            generated = new
            {
                date = DateTime.UtcNow.ToString("dd/MM/yyyy"),
                time = DateTime.UtcNow.ToString("HH:mm")
            },
            originOffice = new
            {
                message = OriginOfficeMessage,
                polId = rate.PolId,
                polCode = rate.PolCode,
                polName = rate.PolName,
                qrContentType = "text/url",
                opensInternalSystem = false,
                publicPageUrl = originOfficePublicUrl,
                qrDataUri = originOfficeQrDataUri
            },
            rate = new
            {
                rateCode = rate.RateCode,
                quoteNumber = Text(rate.QuoNumber, rate.RateCode),
                idtraNumber = Text(rate.IdtraNumber, string.Empty),
                clientName = Text(rate.ClientName),
                agent = showAgent ? reportAgentName : string.Empty,
                showAgent,
                carrier = showCarrier ? Text(rate.CarrierName, "No asignada") : string.Empty,
                showCarrier,
                pol = rate.PolName,
                poe = rate.PoeName,
                pod = rate.PodName,
                route,
                incoterm = Text(rate.IncotermName, Text(rate.IncotermCode, string.Empty)),
                rateType = rate.RateType == Dhole.Pricing.Domain.Rates.Enums.RateType.Spot ? "SPOT" : "TARIFARIO",
                shipmentMode = rate.ShipmentMode.ToString(),
                containerType = shipmentSummary,
                containerQuantity = containers.Sum(x => x.quantity),
                containerSummary = shipmentSummary,
                totalPackages = rate.TotalPackages,
                totalPallets = rate.TotalPallets,
                totalWeightKg = rate.TotalWeightKg,
                totalVolumeCbm = rate.TotalVolumeCbm,
                totalVolume = cftFreight is not null
                    ? $"{(rate.TotalVolumeCbm * MiamiCftPerCbm).ToString("N3", MoneyCulture)} CFT"
                    : $"{rate.TotalVolumeCbm.ToString("N3", MoneyCulture)} CBM",
                kgPerCbm = rate.KgPerCbm,
                chargeableQuantity = miamiBillableCft.HasValue && cftFreight is not null
                    ? ReportQuantity(cftFreight) / MiamiCftPerCbm
                    : rate.ChargeableQuantity,
                cargoDetails,
                pickupLocations,
                currency = currencyValue,
                currencyCode = rate.CurrencyCode,
                hasSingleCurrency,
                hasMultipleCurrencies,
                hasEquivalentCurrencies = canShowEquivalentTotals,
                hasSeparateCurrencyTotals,
                exchangeRateNote,
                freeDays = rate.FreeDays,
                transitTime = string.IsNullOrWhiteSpace(rate.TransitTime) ? "Por confirmar" : rate.TransitTime,
                transitDays = rate.TransitTime,
                validFrom = rate.ValidFrom.ToString("dd/MM/yyyy"),
                validTo = rate.ValidTo.ToString("dd/MM/yyyy"),
                total = reportTotal,
                totalAmount = reportTotalAmount,
                useAllInPresentation = rate.UseAllInPresentation,
                includes = commercialTerms.Includes,
                subjectTo = commercialTerms.SubjectTo,
                excludes = commercialTerms.Excludes,
                status = rate.Status.ToString(),
                rejectionReason = rate.Status == RateStatus.RejectedByClient
                    ? Text(rate.ClosedReason, string.Empty)
                    : string.Empty
            },
            containers,
            items,
            currencyTotals,
            rows
        };

        return JsonSerializer.Serialize(data, JsonOptions);
    }


    private sealed record PickupReportLocation(string Label, string Address, string Classification);

    private static PickupReportLocation[] CreatePickupLocations(
        string? pickupLocationsJson,
        string? legacyPickupAddress,
        string? incotermCode,
        string? incotermName)
    {
        var locations = new List<PickupReportLocation>();

        if (!string.IsNullOrWhiteSpace(pickupLocationsJson))
        {
            try
            {
                using var document = JsonDocument.Parse(pickupLocationsJson);
                if (document.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in document.RootElement.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.Object)
                            continue;

                        var address = PickupStringProperty(item, "Address");
                        if (string.IsNullOrWhiteSpace(address))
                            continue;

                        var cargoCondition = PickupStringProperty(item, "CargoCondition");
                        var classification = cargoCondition switch
                        {
                            "FiscalCargo" => " — Carga fiscal",
                            "NationalizedCargo" => " — Carga nacionalizada",
                            _ => string.Empty
                        };

                        locations.Add(new PickupReportLocation(
                            $"Punto {locations.Count + 1}:",
                            address,
                            classification));
                    }
                }
            }
            catch (JsonException)
            {
                // Los datos históricos pueden tener un JSON inválido. Usar dirección EXW anterior.
            }
        }

        // Las tarifas anteriores a las recolectas múltiples conservan solo PickupAddress.
        // FCA usa este campo para la dirección de entrega: no presentarla como recolecta.
        var isExw = string.Equals(incotermCode, "EXW", StringComparison.OrdinalIgnoreCase)
            || (incotermName?.Contains("EXW", StringComparison.OrdinalIgnoreCase) ?? false)
            || (incotermName?.Contains("Ex Works", StringComparison.OrdinalIgnoreCase) ?? false);
        if (locations.Count == 0 && isExw && !string.IsNullOrWhiteSpace(legacyPickupAddress)
            && !string.Equals(legacyPickupAddress.Trim(), "Recolecta incluida en líneas LCL", StringComparison.OrdinalIgnoreCase))
        {
            locations.Add(new PickupReportLocation("Punto 1:", legacyPickupAddress.Trim(), string.Empty));
        }

        return locations.ToArray();
    }

    private static string? PickupStringProperty(JsonElement location, string propertyName)
    {
        foreach (var property in location.EnumerateObject())
        {
            if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.String)
                return property.Value.GetString()?.Trim();
        }

        return null;
    }

    private static string CreateCargoDetails(string? cargoLinesJson)
    {
        if (string.IsNullOrWhiteSpace(cargoLinesJson))
            return string.Empty;

        try
        {
            using var document = JsonDocument.Parse(cargoLinesJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return string.Empty;

            var details = new List<string>();

            foreach (var line in document.RootElement.EnumerateArray())
            {
                if (line.ValueKind != JsonValueKind.Object)
                    continue;

                JsonElement descriptionElement;
                if (!line.TryGetProperty("Description", out descriptionElement)
                    && !line.TryGetProperty("description", out descriptionElement))
                {
                    continue;
                }

                var description = descriptionElement.ValueKind == JsonValueKind.String
                    ? descriptionElement.GetString()
                    : null;

                if (string.IsNullOrWhiteSpace(description))
                    continue;

                var visibleSegments = Regex.Split(description.Trim(), @"\s+·\s+")
                    .Select(segment => segment.Trim())
                    .Where(segment => !string.IsNullOrWhiteSpace(segment))
                    .Where(segment => !segment.StartsWith("Soportes Pricing ", StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                if (visibleSegments.Length == 0)
                    continue;

                details.Add(string.Join(" · ", visibleSegments));
            }

            return string.Join(
                Environment.NewLine,
                details.Distinct(StringComparer.OrdinalIgnoreCase)
            );
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    // Same physical cargo rule used by Miami quote calculation. LITTLE is volume-only.
    // Do not read ChargeableQuantity: legacy LCL headers clamp it to >= 1 CBM.
    private static decimal? CalculateMiamiBillableCft(RateHeader rate)
    {
        if (rate.ShipmentMode != ShipmentMode.Lcl)
            return null;

        var polCode = (rate.PolCode ?? string.Empty).Trim().ToUpperInvariant();
        var polName = rate.PolName ?? string.Empty;
        if (polCode is not ("MIA" or "USMIA")
            && !polName.Contains("Miami", StringComparison.OrdinalIgnoreCase))
            return null;

        // Only own maritime Miami matrices use the 14.16 kg/CFT rule.
        // An air LCL quote departing Miami can also show CFT, but its billing
        // rules are different and must never be reconciled with this matrix.
        var hasMiamiMatrixFreight = rate.RateDetails.Any(detail =>
            detail.CostDetailType == CostDetailType.Freight
            && detail.ChargeBasis == ChargeBasis.PerChargeableCft
            && (detail.Name.Contains("Flete Miami", StringComparison.OrdinalIgnoreCase)
                || detail.Notes?.Contains("Plan Miami:", StringComparison.OrdinalIgnoreCase) == true));
        if (!hasMiamiMatrixFreight)
            return null;

        decimal billableCbm = 0m;
        if (!string.IsNullOrWhiteSpace(rate.CargoLinesJson))
        {
            try
            {
                var cargoLines = JsonSerializer.Deserialize<RateCargoLineDto[]>(
                    rate.CargoLinesJson, JsonOptions);
                if (cargoLines is { Length: > 0 })
                {
                    billableCbm = cargoLines.Sum(line =>
                        Math.Max(line.BillableVolumeCbm > 0m
                            ? line.BillableVolumeCbm
                            : line.VolumeCbm, 0m));
                }
            }
            catch (JsonException)
            {
                // Historic quotes may contain only their header cargo metrics.
            }
        }

        if (billableCbm <= 0m)
            billableCbm = Math.Max(0m, rate.TotalVolumeCbm);

        var volumeCft = billableCbm * MiamiCftPerCbm;
        var isLittle = rate.RateDetails.Any(detail =>
            detail.Notes?.Contains("Plan Miami: LITTLE", StringComparison.OrdinalIgnoreCase) == true);
        var result = isLittle
            ? volumeCft
            : Math.Max(volumeCft, Math.Max(0m, rate.TotalWeightKg) / MiamiKgPerCft);
        return result > 0m ? result : null;
    }

    private static decimal CalculateAllInAmount(RateHeader rate, IReadOnlyCollection<RateDetail> details, Func<RateDetail, decimal>? quantityResolver = null)
    {
        var targetCurrency = rate.CurrencyCode.Trim().ToUpperInvariant();
        var exchangeRate = rate.ExchangeRateApplied is > 0m
            ? rate.ExchangeRateApplied.Value
            : rate.ExchangeRateSale;
        decimal total = 0m;

        foreach (var detail in details)
        {
            var amount = detail.SaleAmount * (quantityResolver?.Invoke(detail) ?? detail.Quantity);
            var sourceCurrency = detail.CurrencyCode.Trim().ToUpperInvariant();

            if (string.Equals(sourceCurrency, targetCurrency, StringComparison.OrdinalIgnoreCase))
            {
                total += amount;
                continue;
            }

            if (exchangeRate is > 0m && sourceCurrency == "USD" && targetCurrency == "CRC")
            {
                total += amount * exchangeRate.Value;
                continue;
            }

            if (exchangeRate is > 0m && sourceCurrency == "CRC" && targetCurrency == "USD")
            {
                total += amount / exchangeRate.Value;
                continue;
            }

            throw new InvalidOperationException(
                $"No se puede consolidar ALL IN entre {sourceCurrency} y {targetCurrency} sin una conversión compatible."
            );
        }

        return decimal.Round(total, 2, MidpointRounding.AwayFromZero);
    }

    private static string CompactLandEquipmentLabel(string? name, string? code)
    {
        var source = $"{code} {name}";
        var match = Regex.Match(source, @"(?<!\d)(24|26|48|53)(?!\d)", RegexOptions.CultureInvariant);
        if (match.Success)
            return match.Groups[1].Value;

        if (!string.IsNullOrWhiteSpace(name))
            return name.Trim();

        if (!string.IsNullOrWhiteSpace(code))
            return code.Trim();

        return "FTL";
    }

    private string CreateOriginOfficePublicUrl(RateHeader rate)
    {
        var baseAddress = (configuration["Reports:PublicWebBaseAddress"] ?? "https://dhole.customcodecr.com")
            .Trim()
            .TrimEnd('/');
        var polName = rate.PolName.Trim();
        var destinationName = !string.IsNullOrWhiteSpace(rate.PodName)
            ? rate.PodName.Trim()
            : rate.PoeName.Trim();
        var routeKey = $"{polName} - {destinationName}";

        var publicUrl = $"{baseAddress}/origin"
            + $"?pol={Uri.EscapeDataString(polName)}"
            + $"&shipmentMode={Uri.EscapeDataString(rate.ShipmentMode.ToString())}"
            + $"&route={Uri.EscapeDataString(routeKey)}";

        var isLand = rate.ShipmentMode is ShipmentMode.Ftl or ShipmentMode.Ltl;
        var agentCode = isLand ? "GCF" : rate.AgentCode;
        var agentName = isLand ? "Grupo Castro Fallas" : rate.AgentName;

        if (!string.IsNullOrWhiteSpace(agentCode))
            publicUrl += $"&agentCode={Uri.EscapeDataString(agentCode.Trim())}";
        if (!string.IsNullOrWhiteSpace(agentName))
            publicUrl += $"&agent={Uri.EscapeDataString(agentName.Trim())}";

        return publicUrl;
    }

    private static string CreateQrDataUri(string value)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(value, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data);
        var bytes = png.GetGraphic(14);
        return $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
    }

    private static (string Includes, string SubjectTo, string Excludes) ExclusiveCommercialTerms(
        string? includes,
        string? subjectTo,
        string? excludes)
    {
        static string[] Lines(string? value) => string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        static string Key(string value)
        {
            var normalized = Regex.Replace(value.Trim().ToUpperInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();
            var qualifier = Regex.Match(normalized, @"\s(?:USD|EUR|CRC|IVI|IVA|ITBMS|\d)");
            return qualifier.Success && qualifier.Index > 0
                ? normalized[..qualifier.Index].Trim()
                : normalized;
        }

        var included = Lines(includes).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var includedKeys = included.Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var subject = Lines(subjectTo)
            .Where(item => !includedKeys.Contains(Key(item)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var subjectKeys = subject.Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var excluded = Lines(excludes)
            .Where(item => !includedKeys.Contains(Key(item)) && !subjectKeys.Contains(Key(item)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return (string.Join(", ", included), string.Join(", ", subject), string.Join(", ", excluded));
    }
}
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dhole.Pricing.Application.Abstractions.Reports;
using Dhole.Pricing.Contracts.Rates.Response;
using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.Rates;
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
    private const decimal AirPoundsPerKg = 2.20462262m;

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
        var hasSelectedServices = rate.RateServices.Count > 0;
        // Use the persisted service snapshot. Storage days do not imply that a
        // warehouse service was contracted, and a FCL equipment does not imply
        // international transportation on customs/warehouse-only quotations.
        var selectedServiceNames = rate.RateServices
            .Select(service => service.ServiceName.Trim())
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var hasStorageService = rate.RateServices.Any(service =>
                IsStorageService($"{service.ServiceName} {service.ServiceCode}"))
            || rate.RateDetails.Any(detail =>
                IsStorageService(detail.Name) && detail.SaleAmount > 0m);
        var hasTransportService = !hasSelectedServices || rate.RateServices.Any(service =>
            IsTransportationService($"{service.ServiceName} {service.ServiceCode}"));
        var documentTitle = hasStorageService || !hasTransportService
            ? "Cotización de servicios logísticos"
            : "Cotización de transporte internacional";
        var showAgent = true;
        var reportAgentName = UsesRsOriginAgent(rate) ? "RS"
            : isLand ? "Grupo Castro Fallas" : Text(rate.AgentName, "No asignado");
        var showCarrier = rate.ShipmentMode == ShipmentMode.Fcl && hasTransportService;
        var route = !string.IsNullOrWhiteSpace(rate.PodName)
            ? $"{rate.PolName} → {rate.PodName} vía {rate.PoeName}"
            : $"{rate.PolName} → {rate.PoeName}";
        var cargoDetails = CreateCargoDetails(rate.CargoLinesJson);
        var pickupLocations = CreatePickupLocations(
            rate.PickupLocationsJson, rate.PickupAddress, rate.IncotermCode, rate.IncotermName);

        // Air LCL shares ShipmentMode.Lcl with maritime LCL. Detect it using the
        // persisted KG freight basis, cargo flight context or airport IATA route.
        // Do not convert legacy CBM quantities in a PDF: doing so would silently
        // change the customer's payable total without a new commercial quotation.
        var isAirLcl = IsAirLcl(rate);
        var commercial = RateCommercialQuoteSnapshot.Create(rate);
        string ReportDetailName(RateDetail detail) =>
            isAirLcl && detail.CostDetailType == CostDetailType.Freight
                ? "Flete Internacional"
                : detail.Name;

        // These values are calculated once in the canonical Pricing snapshot.
        // The document mapper only formats/prints them; it never resolves a charge basis.
        decimal ReportQuantity(RateDetail detail) => commercial.For(detail).Quantity;
        decimal ReportUnitSale(RateDetail detail) => commercial.For(detail).UnitSale;
        decimal LineSubtotal(RateDetail detail) => commercial.For(detail).SaleSubtotal;
        decimal LineTax(RateDetail detail) => commercial.For(detail).TaxAmount;

        // Visual-only Miami matrix component detail for the customer quote.
        // Reconcile to the persisted cost, never add new RateDetails or sale amounts.
        // Only maritime OWN Miami freight with an explicit commercial plan qualifies;
        // other LCL sources and air-LCL must retain their ordinary PDF.
        string MiamiFreightBreakdown(RateDetail detail)
        {
            if (!miamiBillableCft.HasValue
                || detail.CostDetailType != CostDetailType.Freight
                || detail.ChargeBasis != ChargeBasis.PerChargeableCft
                || (!detail.Name.Contains("Flete Miami", StringComparison.OrdinalIgnoreCase)
                    && detail.Notes?.Contains("Plan Miami:", StringComparison.OrdinalIgnoreCase) != true))
                return string.Empty;

            var match = Regex.Match(detail.Notes ?? string.Empty,
                @"Plan Miami:\s*(NVOCC-A|NVOCC-B|LITTLE|A|B|C|D)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success)
                return string.Empty;

            var parts = match.Groups[1].Value.ToUpperInvariant() switch
            {
                "A" => new[] { ("Flete", 1.75m), ("Bunker", 0.70m), ("THC", 0.35m) },
                "B" => new[] { ("Flete", 1.80m), ("Bunker", 0.80m), ("THC", 0.35m) },
                "C" => new[] { ("Flete", 1.85m), ("Bunker", 0.80m), ("THC", 0.35m) },
                "D" => new[] { ("Flete", 1.85m), ("Bunker", 0.72m), ("THC", 0.35m) },
                "NVOCC-B" => new[] { ("Flete", 1.85m), ("Bunker", 0.70m), ("THC", 0.35m) },
                "NVOCC-A" => new[] { ("Flete", 2.04m), ("GRI", 0.22m), ("THC", 0.34m) },
                _ => Array.Empty<(string Name, decimal Rate)>()
            };
            if (parts.Length == 0)
                return string.Empty; // LITTLE: tariff by bracket, not by CFT components.

            var quantity = ReportQuantity(detail);
            var cost = detail.CostAmount * quantity;
            var weight = parts.Sum(part => part.Item2);
            if (quantity <= 0m || weight <= 0m)
                return string.Empty;

            var lines = new List<string>
            {
                "Desglose de costo del flete Miami (no son cargos adicionales):"
            };
            var allocated = 0m;
            for (var index = 0; index < parts.Length; index++)
            {
                var part = parts[index];
                var componentCost = index == parts.Length - 1
                    ? decimal.Round(cost - allocated, 2, MidpointRounding.AwayFromZero)
                    : decimal.Round(cost * part.Item2 / weight, 2, MidpointRounding.AwayFromZero);
                allocated += componentCost;
                lines.Add($"{part.Item1}: USD {part.Item2.ToString("N2", MoneyCulture)}/CFT"
                    + $" × {quantity.ToString("N3", MoneyCulture)} CFT"
                    + $" = USD {componentCost.ToString("N2", MoneyCulture)}");
            }

            return string.Join("\n", lines);
        }

        var cftFreight = rate.ShipmentMode == ShipmentMode.Lcl
            ? rate.RateDetails.FirstOrDefault(detail =>
                detail.CostDetailType == CostDetailType.Freight &&
                detail.ChargeBasis == ChargeBasis.PerChargeableCft)
            : null;
        // The quote header and its freight line must use the same commercial unit.
        var airKgFreight = isAirLcl
            ? rate.RateDetails.FirstOrDefault(detail =>
                detail.CostDetailType == CostDetailType.Freight
                && detail.ChargeBasis is ChargeBasis.PerKg or ChargeBasis.PerLb)
            : null;
        var lclChargeableLabel = airKgFreight is not null
            ? $"LCL aéreo · {ReportQuantity(airKgFreight).ToString("N3", MoneyCulture)} KG cobrables"
            : cftFreight is not null
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

        // Each RateDetail owns its carrier label; the RateHeader carrier is only a
        // legacy primary-provider value for quotes combining different navieras.
        static string CarrierNote(string? notes, string key)
        {
            var match = Regex.Match(notes ?? string.Empty,
                @"(?:^|[·\r\n])\s*" + Regex.Escape(key) + @"\s*:\s*([^·\r\n]+)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
        }

        string DetailNameWithCarrier(RateDetail detail, string description)
        {
            if (rate.ShipmentMode != ShipmentMode.Fcl || rate.RateContainers.Count < 2)
                return description;
            var carrier = CarrierNote(detail.Notes, "Naviera");
            if (string.IsNullOrWhiteSpace(carrier)) return description;
            var equipment = CarrierNote(detail.Notes, "Equipo");
            if (string.IsNullOrWhiteSpace(equipment) && detail.CostDetailType == CostDetailType.Freight)
            {
                var match = Regex.Match(detail.Notes ?? string.Empty,
                    @"Flete específico para\s+([^·\r\n]+)",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (match.Success) equipment = match.Groups[1].Value.Trim();
            }
            return string.IsNullOrWhiteSpace(equipment)
                ? $"{description} · Naviera: {carrier}"
                : $"{description} · Naviera: {carrier} · Equipo: {equipment}";
        }

        string ClientDetailNotes(RateDetail detail)
        {
            if (detail.CostDetailType == CostDetailType.Insurance) return string.Empty;
            var notes = detail.Notes ?? string.Empty;
            if (rate.ShipmentMode != ShipmentMode.Fcl || rate.RateContainers.Count < 2)
                return notes.Trim();
            notes = Regex.Replace(notes,
                @"(?:^|\s*·\s*)(?:NavieraId|Naviera|Equipo|Tarifa importada)\s*:\s*[^·\r\n]+",
                "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return notes.Trim().Trim('·', ' ', '\r', '\n');
        }

        var freightCarrierNames = rate.ShipmentMode == ShipmentMode.Fcl
            ? rate.RateDetails.Where(detail => detail.CostDetailType == CostDetailType.Freight)
                .Select(detail => CarrierNote(detail.Notes, "Naviera"))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : Array.Empty<string>();
        var reportCarrierName = freightCarrierNames.Length > 1
            ? string.Join(" / ", freightCarrierNames)
            : Text(rate.CarrierName, "No asignada");

        // Keep every persisted line in tabular exports and in the Pricing UI.
        // The customer-facing PDF alone hides charges with zero sale value.
        // Filtering zero-value lines cannot change the payable total or taxes.
        var persistedDetails = rate.RateDetails
            .OrderBy(x => x.CostDetailType)
            .ThenBy(x => x.Name)
            .ToArray();
        var reportDetails = persistedDetails
            .Where(detail => commercial.For(detail).ShowOnClientPdf)
            .ToArray();

        // PDF receives finalized commercial amounts, including line IVA and ALL IN.
        string TaxLabel(RateDetail detail) => LineTax(detail) > 0m
            ? $"IVA {detail.DestinationTaxRate.ToString("0.##", MoneyCulture)}%: {DetailMoney(detail, LineTax(detail))}"
            : string.Empty;

        var useAllInPresentation = commercial.UseAllIn;
        var allInAmount = commercial.AllInSubtotal;
        var allInTax = commercial.AllInTax;
        var hasDestinationTax = reportDetails.Any(detail => commercial.For(detail).TaxAmount > 0m);

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
                    // Values consumed by the client-facing PDF's IVA and payable-total columns.
                    lineTaxAmount = allInTax,
                    lineTotalWithTaxAmount = allInAmount + allInTax,
                    destinationTaxLabel = allInTax > 0m ? $"IVA: {currencyValue} {allInTax.ToString("N2", MoneyCulture)}" : string.Empty,
                    miamiBreakdown = string.Empty,
                    notes = string.Empty
                }
            }
            : reportDetails
                .Select(detail => new
                {
                    description = DetailNameWithCarrier(detail, ReportDetailName(detail)),
                    quantity = ReportQuantity(detail),
                    currency = CurrencyValue(detail.CurrencyName, detail.CurrencyCode),
                    currencyCode = detail.CurrencyCode,
                    unitSale = DetailMoney(detail, ReportUnitSale(detail)),
                    unitSaleAmount = ReportUnitSale(detail),
                    lineTotal = DetailMoney(detail, LineSubtotal(detail)),
                    lineTotalAmount = LineSubtotal(detail),
                    lineTaxAmount = LineTax(detail),
                    lineTotalWithTaxAmount = commercial.For(detail).SaleTotal,
                    destinationTaxLabel = TaxLabel(detail),
                    miamiBreakdown = MiamiFreightBreakdown(detail),
                    notes = ClientDetailNotes(detail)
                })
                .ToArray();

        // All grouping, FX conversion and totals have already been resolved
        // in Pricing. This layer has no independent financial formulas.
        var currencyTotals = commercial.CurrencyTotals.Select(total => new
        {
            currency = total.Currency,
            currencyCode = total.CurrencyCode,
            subtotalAmount = total.Subtotal,
            taxAmount = total.Tax,
            amount = total.Amount,
            subtotal = $"{total.Currency} {total.Subtotal.ToString("N2", MoneyCulture)}",
            tax = $"{total.Currency} {total.Tax.ToString("N2", MoneyCulture)}",
            total = $"{total.Currency} {total.Amount.ToString("N2", MoneyCulture)}"
        }).ToArray();
        var hasSingleCurrency = currencyTotals.Length == 1;
        var hasMultipleCurrencies = currencyTotals.Length > 1;
        var canShowEquivalentTotals = commercial.HasEquivalentCurrencies;
        var hasSeparateCurrencyTotals = hasMultipleCurrencies && !canShowEquivalentTotals;
        var exchangeRateNote = canShowEquivalentTotals && commercial.AppliedExchangeRate is > 0m
            ? $"Tipo de cambio aplicado: CRC {commercial.AppliedExchangeRate.Value.ToString("N4", MoneyCulture)} por USD 1.00"
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
            : persistedDetails
                .Select(detail => new Dictionary<string, object?>
                {
                    ["Concepto"] = DetailNameWithCarrier(detail, ReportDetailName(detail)),
                    ["Cantidad"] = ReportQuantity(detail),
                    ["Moneda"] = CurrencyValue(detail.CurrencyName, detail.CurrencyCode),
                    ["Precio unitario"] = ReportUnitSale(detail),
                    ["Total"] = LineSubtotal(detail),
                    ["Notas"] = ClientDetailNotes(detail)
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
                revisionNumber = Math.Max(1, rate.RevisionNumber),
                idtraNumber = Text(rate.IdtraNumber, string.Empty),
                clientName = Text(rate.ClientName),
                agent = showAgent ? reportAgentName : string.Empty,
                showAgent,
                carrier = showCarrier ? reportCarrierName : string.Empty,
                showCarrier,
                pol = rate.PolName,
                poe = rate.PoeName,
                pod = rate.PodName,
                route,
                incoterm = Text(rate.IncotermName, Text(rate.IncotermCode, string.Empty)),
                rateType = rate.RateType == Dhole.Pricing.Domain.Rates.Enums.RateType.Spot ? "SPOT" : "TARIFARIO",
                documentTitle,
                hasTransportService,
                hasSelectedServices,
                servicesSummary = string.Join(", ", selectedServiceNames),
                hasStorageService,
                storageDays = rate.StorageDays,
                storageDaysLabel = rate.StorageDays is > 0
                    ? $"{rate.StorageDays} {(rate.StorageDays == 1 ? "día" : "días")}"
                    : "Por definir",
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
                hasDestinationTax,
                hasSeparateCurrencyTotals,
                exchangeRateNote,
                freeDays = rate.FreeDays,
                transitTime = string.IsNullOrWhiteSpace(rate.TransitTime) ? "Por confirmar" : rate.TransitTime,
                transitDays = rate.TransitTime,
                validFrom = rate.ValidFrom.ToString("dd/MM/yyyy"),
                validTo = rate.ValidTo.ToString("dd/MM/yyyy"),
                total = reportTotal,
                totalAmount = reportTotalAmount,
                useAllInPresentation,
                allInFallbackToDetails = rate.UseAllInPresentation && !useAllInPresentation,
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


    private static bool IsStorageService(string value)
    {
        var normalized = NormalizeServiceLabel(value);
        return normalized.Contains("ALMACEN", StringComparison.Ordinal)
            || normalized.Contains("DEPOSITO FISCAL", StringComparison.Ordinal)
            || normalized.Contains("BODEGA FISCAL", StringComparison.Ordinal);
    }

    private static bool IsTransportationService(string value)
    {
        var normalized = NormalizeServiceLabel(value);
        return normalized.Contains("TRANSPORTE", StringComparison.Ordinal)
            || normalized.Contains("FLETE", StringComparison.Ordinal);
    }

    private static string NormalizeServiceLabel(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var decomposed = text.Normalize(System.Text.NormalizationForm.FormD);
        return new string(decomposed
            .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            .ToArray()).ToUpperInvariant();
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

                // Older LCL quotations may contain the same observations appended
                // multiple times inside a single cargo description. Remove only
                // identical observation blocks, keeping other details and cargo lines.
                var seenObservations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var uniqueSegments = visibleSegments
                    .Where(segment =>
                    {
                        if (!Regex.IsMatch(segment, @"^Observaciones\s*:", RegexOptions.IgnoreCase))
                            return true;

                        var key = Regex.Replace(segment, @"\s+", " ").Trim();
                        return seenObservations.Add(key);
                    })
                    .ToArray();

                details.Add(string.Join(" · ", uniqueSegments));
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

    private static bool CanConsolidateAllIn(
        RateHeader rate,
        IReadOnlyCollection<RateDetail> details,
        Func<string?, string?, string> currencyIso)
    {
        var targetCurrency = currencyIso(rate.CurrencyCode, rate.CurrencyName);
        var exchangeRate = rate.ExchangeRateApplied is > 0m
            ? rate.ExchangeRateApplied
            : rate.ExchangeRateSale;

        return details.All(detail =>
        {
            var sourceCurrency = currencyIso(detail.CurrencyCode, detail.CurrencyName);
            if (string.Equals(sourceCurrency, targetCurrency, StringComparison.OrdinalIgnoreCase))
                return true;

            return exchangeRate is > 0m
                && (sourceCurrency is "USD" or "CRC")
                && (targetCurrency is "USD" or "CRC");
        });
    }

    private static decimal CalculateAllInAmount(
        RateHeader rate,
        IReadOnlyCollection<RateDetail> details,
        Func<string?, string?, string> currencyIso,
        Func<RateDetail, decimal>? quantityResolver = null,
        Func<RateDetail, decimal>? amountResolver = null)
    {
        var targetCurrency = currencyIso(rate.CurrencyCode, rate.CurrencyName);
        var exchangeRate = rate.ExchangeRateApplied is > 0m
            ? rate.ExchangeRateApplied.Value
            : rate.ExchangeRateSale;
        decimal total = 0m;

        foreach (var detail in details)
        {
            var amount = amountResolver?.Invoke(detail)
                ?? detail.SaleAmount * (quantityResolver?.Invoke(detail) ?? detail.Quantity);
            var sourceCurrency = currencyIso(detail.CurrencyCode, detail.CurrencyName);

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

    // ShipmentMode.Lcl is also used for air cargo. RS is the origin agent only
    // for maritime own-consolidation LCL; the commercial source remains GCF.
    private static bool IsAirLcl(RateHeader rate) =>
        rate.ShipmentMode == ShipmentMode.Lcl &&
        (rate.RateDetails.Any(detail =>
            detail.CostDetailType == CostDetailType.Freight &&
            detail.ChargeBasis is ChargeBasis.PerKg or ChargeBasis.Per100Kg)
         || Regex.IsMatch(rate.CargoLinesJson ?? string.Empty,
             @"a[eé]re[ao]|aviaci[oó]n|aeropuerto|airport|vuelos?",
             RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
         || (Regex.IsMatch(rate.PolName ?? string.Empty, @"\([A-Z]{3}\)")
             && Regex.IsMatch(rate.PoeName ?? string.Empty, @"\([A-Z]{3}\)")));

    private static bool UsesRsOriginAgent(RateHeader rate)
    {
        if (rate.ShipmentMode != ShipmentMode.Lcl || IsAirLcl(rate)) return false;
        // Source notes identify modern LCL quotes and take precedence over legacy
        // GCF header fields, so a GCF coloader cannot be mistaken for own LCL.
        if (rate.RateDetails.Any(detail =>
            detail.Notes?.Contains("LCL COLOADER", StringComparison.OrdinalIgnoreCase) == true))
            return false;
        if (rate.RateDetails.Any(detail =>
            detail.Notes?.Contains("LCL PROPIO", StringComparison.OrdinalIgnoreCase) == true
            || detail.Notes?.Contains("ConsolidadoId:", StringComparison.OrdinalIgnoreCase) == true))
            return true;
        // Old own LCL quotations did not always persist the source marker.
        return string.Equals(rate.AgentCode?.Trim(), "GCF", StringComparison.OrdinalIgnoreCase)
            || string.Equals(rate.AgentName?.Trim(), "GCF", StringComparison.OrdinalIgnoreCase)
            || string.Equals(rate.AgentName?.Trim(), "Grupo Castro Fallas", StringComparison.OrdinalIgnoreCase);
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
        var usesRs = UsesRsOriginAgent(rate);
        var agentCode = usesRs ? "RS" : isLand ? "GCF" : rate.AgentCode;
        var agentName = usesRs ? "RS" : isLand ? "Grupo Castro Fallas" : rate.AgentName;

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
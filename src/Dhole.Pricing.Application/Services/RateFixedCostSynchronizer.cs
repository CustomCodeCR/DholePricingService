using System.Globalization;
using System.Text;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Domain.Costs.Entities;
using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Domain.Shared;

namespace Dhole.Pricing.Application.Services;

public sealed class RateFixedCostSynchronizer(
    ICostRepository costs,
    IPricingConfigCatalogClient configCatalog,
    ICostRoutePortSelectionStore costSelections
) : IRateFixedCostSynchronizer
{
    private const decimal PanamaGamInternationalLandFreight = 2140m;

    // No existe FK desde RateDetail.CostId hacia Costs. Este identificador estable permite
    // tratar la regla de ruta como un costo fijo automático y resincronizarla sin duplicados.
    private static readonly Guid PanamaGamLandFreightRuleId =
        Guid.Parse("A2140000-0000-4000-8000-000000000001");

    public async Task SynchronizeAsync(
        RateHeader rate,
        Guid? updatedBy,
        CancellationToken cancellationToken = default,
        bool preserveExplicitFixedDetails = false
    )
    {
        // Clean historical identical Release rows when a rate is next saved.
        RateReleaseLineDeduplicator.RemoveExactDuplicates(rate, updatedBy);

        // Las tarifas LCL coloader no usan una naviera propia. Pricing normaliza
        // CarrierId vacío a la referencia interna "No aplica (LCL)" (CarrierCode=LCL).
        // Las líneas del tarifario del coloader y las agregadas manualmente son la
        // fuente autoritativa: no agregarles cargos fijos automáticos de navieras.
        // Los detalles excluidos se eliminan mediante RemovedExtraDetailIds al editar.
        var hasExplicitColoaderSource = rate.RateDetails.Any(detail =>
            (detail.Notes ?? string.Empty).Contains("Fuente LCL: Coloader", StringComparison.OrdinalIgnoreCase)
            || (detail.Notes ?? string.Empty).Contains("LCL COLOADER", StringComparison.OrdinalIgnoreCase));
        if (rate.ShipmentMode == ShipmentMode.Lcl
            && string.Equals(rate.CarrierCode, "LCL", StringComparison.OrdinalIgnoreCase)
            && (hasExplicitColoaderSource || !IsOwnLclRate(rate)))
        {
            return;
        }

        // LCL propio ya lleva la matriz del consolidado prorrateada dentro del flete/CBM.
        // En la tarifa comercial solo deben persistir el flete y las líneas adicionales
        // provenientes de las reglas/Excel, que no están ligadas a CostId.
        if (IsOwnLclRate(rate))
        {
            // Own LCL consolidations must not import automatic fixed catalog costs.
            // However, user-selected variable charges (e.g. an EXW pickup/recolecta)
            // are explicitly included in the quote and must never be deleted just
            // because they retain a CostId referencing the Costs catalog.
            var configuredDetailIds = rate.RateDetails
                .Where(x => x.CostId.HasValue && x.CostType == CostType.Fixed)
                .Select(x => x.Id)
                .ToArray();

            foreach (var detailId in configuredDetailIds)
            {
                rate.RemoveRateDetail(detailId, updatedBy);
            }

            return;
        }

        var existingAmounts = rate
            .RateDetails.Where(x => x.CostId.HasValue && x.CostType == CostType.Fixed)
            .GroupBy(x => x.CostId!.Value)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var detail = group.First();
                    return (
                        detail.CostAmount,
                        detail.SaleAmount,
                        detail.Quantity,
                        detail.CurrencyId,
                        detail.CurrencyName,
                        detail.CurrencyCode,
                        detail.ApplyDestinationTax,
                        detail.DestinationTaxRate,
                        detail.BillToClient
                    );
                }
            );

        // Al crear/editar una tarifa desde una importación, el wizard ya resolvió y envió
        // explícitamente los rubros que el usuario está cotizando. En rutas sintéticas como
        // "Multimodal Via Panamá" esos rubros pueden estar asociados al POE real de la tarifa
        // importada y no al POE sintético del header. Eliminarlos y volverlos a filtrar aquí
        // hacía desaparecer BL, cargos de destino, origen, recolecta, etc. Los snapshots que
        // llegaron explícitamente tienen prioridad; solo completamos costos automáticos que no
        // hayan sido enviados.
        preserveExplicitFixedDetails = preserveExplicitFixedDetails || rate.SourceImportFclRateId.HasValue;
        if (!preserveExplicitFixedDetails)
        {
            rate.RemoveAutomaticFixedDetails(updatedBy);
        }

        var activeFixedCosts = await costs.GetActiveCostsAsync(
            costType: CostType.Fixed,
            cancellationToken: cancellationToken
        );
        var activeFixedCostSelections = await costSelections.GetManyAsync(
            activeFixedCosts.Select(x => x.Id).ToArray(),
            cancellationToken
        );

        var hasExplicitFreight = rate.RateDetails.Any(x =>
            x.CostDetailType == CostDetailType.Freight && !x.CostId.HasValue
        );
        // Los Incoterms CFR, CIF, CPT, CIP, DAP, DPU y DDP no deben incorporar
        // flete automático aunque Transporte Internacional siga seleccionado.
        var excludesQuotedFreight = System.Text.RegularExpressions.Regex.IsMatch(
            $"{rate.IncotermCode} {rate.IncotermName}",
            @"(?<![A-Za-z])(?:CFR|CIF|CPT|CIP|DAP|DPU|DDP)(?![A-Za-z])",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
                | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        var exchangeRateSale = rate.ExchangeRateApplied is > 0m
            ? rate.ExchangeRateApplied.Value
            : rate.ExchangeRateSale is > 0m
                ? rate.ExchangeRateSale.Value
                : 0m;
        PricingConfigCatalogItem? crcCurrency = null;

        foreach (var cost in activeFixedCosts.Where(cost =>
        {
            activeFixedCostSelections.TryGetValue(cost.Id, out var selection);
            return MatchesRate(cost, rate, selection)
                && !(excludesQuotedFreight && cost.CostDetailType == CostDetailType.Freight)
                && !(hasExplicitFreight && cost.CostDetailType == CostDetailType.Freight)
                && (!preserveExplicitFixedDetails || !existingAmounts.ContainsKey(cost.Id));
        }))
        {
            // Resolve the band against persisted cargo before any currency conversion or
            // ChargeBasis multiplication. A missing band must not create a zero-priced item.
            if (!TryResolveRateAmounts(cost, rate, out var resolvedCostAmount, out var resolvedSaleAmount))
                continue;

            activeFixedCostSelections.TryGetValue(cost.Id, out var costSelection);
            var isAgentAssociated = cost.AgentId.HasValue || costSelection?.AgentIds.Count > 0;
            var suppressAgentSale = isAgentAssociated && cost.CostDetailType != CostDetailType.OriginCharge;
            var hasExistingAmount = existingAmounts.TryGetValue(cost.Id, out var existingAmount);
            var forceCrc = CostaRicaServiceCurrencyRules.RequiresCrc(cost, rate);

            Guid targetCurrencyId;
            string targetCurrencyName;
            string targetCurrencyCode;

            if (forceCrc)
            {
                crcCurrency ??= await configCatalog.GetActiveByCodeAsync(
                    PricingConstants.CatalogSlugs.Currencies,
                    "CRC",
                    cancellationToken
                );
                if (crcCurrency is null)
                    throw new InvalidOperationException(
                        "No se encontró la moneda CRC activa en el catálogo 'currencies' de Config."
                    );

                targetCurrencyId = crcCurrency.Id;
                targetCurrencyName = crcCurrency.SnapshotName();
                targetCurrencyCode = crcCurrency.Code;
            }
            else if (hasExistingAmount)
            {
                // Si Pricing escogió una moneda por línea, conservarla al resincronizar el fijo.
                targetCurrencyId = existingAmount.CurrencyId;
                targetCurrencyName = existingAmount.CurrencyName;
                targetCurrencyCode = existingAmount.CurrencyCode;
            }
            else
            {
                targetCurrencyId = cost.CurrencyId;
                targetCurrencyName = cost.CurrencyName;
                targetCurrencyCode = cost.CurrencyCode;
            }

            // El costo contable de un fijo siempre parte del maestro y se convierte a la moneda
            // de la línea. La venta puede conservar el override de Pricing, convirtiéndolo si hace falta.
            var costAmount = CostaRicaServiceCurrencyRules.ConvertUsdCrc(
                resolvedCostAmount, cost.CurrencyCode, targetCurrencyCode, exchangeRateSale);
            // Pantalla 7 is authoritative for the sale of a detail already present in
            // the quotation. PricingRulesJson and minimum rules may recalculate
            // catalog costs, but must not discard the sale entered by the user.
            // MinimumSaleAmount is still enforced on effectiveSaleTotal below.
            var saleAmount = suppressAgentSale
                ? 0m
                : hasExistingAmount
                    ? CostaRicaServiceCurrencyRules.ConvertUsdCrc(
                        existingAmount.SaleAmount,
                        existingAmount.CurrencyCode,
                        targetCurrencyCode,
                        exchangeRateSale)
                    : CostaRicaServiceCurrencyRules.ConvertUsdCrc(
                        resolvedSaleAmount, cost.CurrencyCode, targetCurrencyCode, exchangeRateSale);

            var minimumCostAmount = cost.MinimumCostAmount.HasValue
                ? CostaRicaServiceCurrencyRules.ConvertUsdCrc(
                    cost.MinimumCostAmount.Value, cost.CurrencyCode, targetCurrencyCode, exchangeRateSale)
                : 0m;
            var minimumSaleAmount = cost.MinimumSaleAmount.HasValue
                ? CostaRicaServiceCurrencyRules.ConvertUsdCrc(
                    cost.MinimumSaleAmount.Value, cost.CurrencyCode, targetCurrencyCode, exchangeRateSale)
                : 0m;

            var quantity = rate.ResolveChargeQuantity(cost.ChargeBasis, kgPerCbmOverride: cost.KgPerCbm);
            var effectiveCostTotal = Math.Max(costAmount * quantity, minimumCostAmount);
            var effectiveSaleTotal = suppressAgentSale
                ? 0m
                : Math.Max(saleAmount * quantity, minimumSaleAmount);
            var effectiveCostAmount = quantity > 0m ? effectiveCostTotal / quantity : effectiveCostTotal;
            var effectiveSaleAmount = quantity > 0m ? effectiveSaleTotal / quantity : effectiveSaleTotal;

            // Two distinct CostIds may represent the same Release charge for the same
            // shipment. Keep the explicit/persisted line instead of billing it twice.
            if (RateReleaseLineDeduplicator.HasEquivalent(
                rate,
                cost.Name,
                cost.ChargeBasis,
                targetCurrencyCode,
                effectiveCostAmount,
                effectiveSaleAmount,
                cost.Notes,
                quantity,
                hasExistingAmount ? existingAmount.BillToClient : null,
                hasExistingAmount && existingAmount.ApplyDestinationTax,
                hasExistingAmount ? existingAmount.DestinationTaxRate : 0m))
            {
                continue;
            }

            var synchronizedDetail = rate.AddRateDetail(
                rate.Id,
                cost.Id,
                cost.Name,
                cost.CostDetailType,
                cost.CostType,
                cost.ChargeBasis,
                targetCurrencyId,
                targetCurrencyName,
                targetCurrencyCode,
                effectiveCostAmount,
                effectiveSaleAmount,
                cost.Notes,
                quantity,
                updatedBy
            );

            if (hasExistingAmount)
            {
                synchronizedDetail.ConfigureDestinationTax(
                    existingAmount.ApplyDestinationTax,
                    existingAmount.DestinationTaxRate
                );
                synchronizedDetail.ConfigureBillToClient(existingAmount.BillToClient);
            }
        }

        await AddPanamaGamInternationalLandFreightAsync(rate, updatedBy, cancellationToken);
    }

    private async Task AddPanamaGamInternationalLandFreightAsync(
        RateHeader rate,
        Guid? updatedBy,
        CancellationToken cancellationToken
    )
    {
        if (rate.ShipmentMode != ShipmentMode.Fcl || !IsPanamaToGam(rate))
            return;

        // A Panamá POE and GAM POD do not by themselves mean that the client
        // contracted a land freight. Customs-only or warehouse-only quotes must
        // never receive the historical synthetic international-land charge.
        if (!rate.RateServices.Any(service =>
            NormalizeRouteText($"{service.ServiceName} {service.ServiceCode}").Contains("TRANSPORTE")))
            return;

        // Never duplicate a configured catalog freight charge with the historical
        // synthetic Panamá -> GAM amount.
        if (rate.RateDetails.Any(detail =>
            detail.CostId.HasValue
            && detail.CostDetailType == CostDetailType.InlandTransport
            && (NormalizeRouteText(detail.Name).Contains("FLETE TERRESTRE")
                || NormalizeRouteText(detail.Name).Contains("FLETE INTERNACIONAL TERRESTRE"))))
            return;

        if (rate.RateDetails.Any(x => x.CostId == PanamaGamLandFreightRuleId))
            return;

        Guid currencyId;
        string currencyName;
        string currencyCode;

        if (string.Equals(rate.CurrencyCode, "USD", StringComparison.OrdinalIgnoreCase))
        {
            currencyId = rate.CurrencyId;
            currencyName = rate.CurrencyName;
            currencyCode = rate.CurrencyCode;
        }
        else
        {
            var usd = await configCatalog.GetActiveByCodeAsync(
                PricingConstants.CatalogSlugs.Currencies,
                "USD",
                cancellationToken
            );

            if (usd is null)
                throw new InvalidOperationException(
                    "No se encontró la moneda USD activa en el catálogo 'currencies' de Config."
                );

            currencyId = usd.Id;
            currencyName = usd.Name;
            currencyCode = usd.Code;
        }

        rate.AddRateDetail(
            rate.Id,
            PanamaGamLandFreightRuleId,
            "Flete internacional terrestre",
            CostDetailType.InlandTransport,
            CostType.Fixed,
            ChargeBasis.PerContainer,
            currencyId,
            currencyName,
            currencyCode,
            PanamaGamInternationalLandFreight,
            PanamaGamInternationalLandFreight,
            "Aplicado automáticamente para la ruta POE Panamá → POD GAM.",
            rate.ContainerQuantity,
            updatedBy
        );
    }

    private static bool IsOwnLclRate(RateHeader rate)
    {
        if (rate.ShipmentMode != ShipmentMode.Lcl)
            return false;

        var headerMarker = NormalizeRouteText(rate.RateName);
        if (headerMarker.Contains("LCL PROPIO", StringComparison.Ordinal)
            || headerMarker.Contains("CONSOLIDADO PROPIO", StringComparison.Ordinal))
            return true;

        return rate.RateDetails.Any(detail =>
        {
            if (detail.CostId.HasValue)
                return false;

            var marker = NormalizeRouteText($"{detail.Name} {detail.Notes}");
            if (marker.Contains("LCL PROPIO", StringComparison.Ordinal)
                || marker.Contains("CONSOLIDADO PROPIO", StringComparison.Ordinal))
                return true;

            // Las líneas calculadas por OwnLclConsolidationEndpoints conservan esta nota
            // para bases HBL/SET. Es la señal de que la tarifa viene exclusivamente de la
            // matriz Excel del consolidado propio y no debe mezclarse con Costos y recargos.
            return detail.CostType == CostType.Variable
                && marker.Contains("BASE DEL EXCEL", StringComparison.Ordinal);
        });
    }

    private static bool TryResolveRateAmounts(
        Cost cost,
        RateHeader rate,
        out decimal amountCost,
        out decimal amountSale)
    {
        // For a mixed 20'/40' FCL shipment, evaluate each physical container's
        // price independently. Applying the primary equipment price multiplied by
        // the total container count would silently misquote mixed equipment.
        var allocations = rate.RateContainers
            .Where(x => x.Quantity > 0)
            .GroupBy(x => x.ContainerTypeId)
            .Select(g => (TypeId: g.Key, Quantity: g.Sum(x => x.Quantity)))
            .ToArray();

        if (CostPricingRules.UsesContainerType(cost.PricingRulesJson)
            && allocations.Length > 1)
        {
            amountCost = 0m;
            amountSale = 0m;
            if (cost.ChargeBasis is not (ChargeBasis.PerContainer or ChargeBasis.PerTruck
                or ChargeBasis.PerShipment or ChargeBasis.PerService))
                return false;

            foreach (var allocation in allocations)
            {
                if (!CostPricingRules.TryResolve(
                    cost.PricingRulesJson,
                    rate.TotalWeightKg > 0 ? rate.TotalWeightKg : null,
                    rate.TotalPackages > 0 ? rate.TotalPackages : null,
                    allocation.TypeId,
                    cost.CostAmount, cost.SaleAmount,
                    out var unitCost, out var unitSale, rate.StorageDays))
                    return false;

                amountCost += unitCost * allocation.Quantity;
                amountSale += unitSale * allocation.Quantity;
            }

            if (cost.ChargeBasis is ChargeBasis.PerContainer or ChargeBasis.PerTruck)
            {
                var totalQuantity = allocations.Sum(x => x.Quantity);
                amountCost /= totalQuantity;
                amountSale /= totalQuantity;
            }

            return true;
        }

        return CostPricingRules.TryResolve(
            cost.PricingRulesJson,
            rate.TotalWeightKg > 0 ? rate.TotalWeightKg : null,
            rate.TotalPackages > 0 ? rate.TotalPackages : null,
            rate.ContainerTypeId != Guid.Empty ? rate.ContainerTypeId : null,
            cost.CostAmount, cost.SaleAmount,
            out amountCost, out amountSale, rate.StorageDays);
    }

    private static bool MatchesRate(
        Cost cost,
        RateHeader rate,
        CostRoutePortSelectionSet? selection
    )
    {
        var matchesAgent = SelectionMatches(selection?.AgentIds, cost.AgentId, rate.AgentId);
        var matchesCarrier = SelectionMatches(selection?.CarrierIds, cost.CarrierId, rate.CarrierId);
        var matchesMode = cost.AppliesToShipmentMode(rate.ShipmentMode);
        var matchesIncoterm =
            cost.Incoterms.Count == 0
            || (
                rate.IncotermId.HasValue
                && cost.Incoterms.Any(x => x.IncotermId == rate.IncotermId.Value)
            );
        var matchesServices =
            cost.Services.Count == 0
            || cost.Services.Any(costService =>
                rate.RateServices.Any(rateService => rateService.ServiceId == costService.ServiceId));

        if (!matchesAgent || !matchesCarrier || !matchesMode || !matchesIncoterm || !matchesServices)
            return false;

        // A warehouse tariff advertised as covering the first five/seven days
        // cannot be applied until the operator entered the actual fiscal days.
        // The carrier FreeDays field must never be used as a fallback.
        if (cost.Services.Any(service =>
        {
            var serviceName = NormalizeRouteText($"{service.ServiceName} {service.ServiceCode}");
            return serviceName.Contains("ALMACEN")
                || serviceName.Contains("DEPOSITO FISCAL")
                || serviceName.Contains("BODEGA FISCAL");
        }) && rate.StorageDays is not > 0)
            return false;

        if (!SelectionMatches(selection?.PolIds, cost.PolId, rate.PolId))
            return false;
        if (!SelectionMatches(selection?.PoeIds, cost.PoeId, rate.PoeId))
            return false;
        if (!SelectionMatches(selection?.PodIds, cost.PodId, rate.PodId))
            return false;

        var hasStructuredRoute =
            selection?.PolIds.Count > 0
            || selection?.PoeIds.Count > 0
            || selection?.PodIds.Count > 0
            || cost.PolId.HasValue
            || cost.PoeId.HasValue
            || cost.PodId.HasValue;
        if (hasStructuredRoute)
            return true;

        if (!cost.PortId.HasValue)
            return true;

        return cost.PortRole switch
        {
            CostPortRole.Pol => cost.PortId == rate.PolId,
            CostPortRole.Poe => cost.PortId == rate.PoeId,
            CostPortRole.Pod => cost.PortId == rate.PodId,
            CostPortRole.Any =>
                cost.PortId == rate.PolId || cost.PortId == rate.PoeId || cost.PortId == rate.PodId,
            null => cost.PortId == rate.PolId || cost.PortId == rate.PoeId || cost.PortId == rate.PodId,
            _ => false,
        };
    }

    private static bool SelectionMatches(
        IReadOnlyCollection<Guid>? selectedIds,
        Guid? legacyId,
        Guid? actualId
    )
    {
        if (selectedIds is { Count: > 0 })
            return actualId.HasValue && selectedIds.Contains(actualId.Value);

        return !legacyId.HasValue || legacyId == actualId;
    }

    private static bool IsPanamaToGam(RateHeader rate)
    {
        var poe = NormalizeRouteText($"{rate.PoeCode} {rate.PoeName}");
        var pod = NormalizeRouteText($"{rate.PodCode} {rate.PodName}");

        var isPanamaPoe = new[]
        {
            "PANAMA",
            "COLON",
            "MANZANILLO",
            "RODMAN",
            "CRISTOBAL",
            "BALBOA",
        }.Any(poe.Contains);

        var podTokens = pod.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var isGamPod = podTokens.Contains("GAM", StringComparer.Ordinal)
            || pod.Contains("GRAN AREA METROPOLITANA", StringComparison.Ordinal);

        return isPanamaPoe && isGamPod;
    }

    private static string NormalizeRouteText(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;

            builder.Append(char.IsLetterOrDigit(character) ? char.ToUpperInvariant(character) : ' ');
        }

        return string.Join(
            ' ',
            builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)
        );
    }
}
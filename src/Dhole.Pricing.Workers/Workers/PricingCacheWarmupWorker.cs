using CustomCodeFramework.Workers.Abstractions;
using Dhole.Pricing.Application.Abstractions.Cache;
using Dhole.Pricing.Contracts.Costs.Response;
using Dhole.Pricing.Contracts.Imports.Response;
using Dhole.Pricing.Contracts.Rates.Response;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Worker.Workers;

internal sealed class PricingCacheWarmupWorker(
    ServiceDbContext dbContext,
    ICostCacheService costCache,
    IImportRateCacheService importRateCache,
    IRateHeaderCacheService rateHeaderCache,
    IConfiguration configuration,
    ILogger<PricingCacheWarmupWorker> logger
) : IBackgroundWorker
{
    private static readonly SemaphoreSlim ExecutionGate = new(1, 1);

    public string Name => "pricing.cache-warmup";

    public async Task ExecuteAsync(
        IWorkerExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        if (!configuration.GetValue<bool>("Pricing:CacheWarmup:Enabled"))
        {
            return;
        }

        // Evita dos warmups simultáneos si una ejecución tarda más que el intervalo
        // configurado del scheduler. No esperar aquí es intencional: una ejecución
        // solapada no aporta nada y duplica el uso de memoria.
        if (!await ExecutionGate.WaitAsync(0, cancellationToken))
        {
            logger.LogWarning("Pricing cache warmup omitido porque ya existe una ejecución activa.");
            return;
        }

        try
        {
            logger.LogInformation("Pricing cache warmup started.");

            await WarmCostsAsync(cancellationToken);
            await WarmImportRatesAsync(cancellationToken);
            await WarmRateHeadersAsync(cancellationToken);

            logger.LogInformation("Pricing cache warmup completed.");
        }
        finally
        {
            ExecutionGate.Release();
        }
    }

    private async Task WarmCostsAsync(CancellationToken cancellationToken)
    {
        var total = await dbContext.Costs.AsNoTracking().CountAsync(x => !x.IsDeleted, cancellationToken);
        var limit = ReadPositiveInt("Pricing:CacheWarmup:MaxCosts", 1000);
        if (total > limit)
        {
            logger.LogWarning(
                "Se omite warmup de Costs: {Total} registros exceden el límite seguro {Limit}. El cache se cargará bajo demanda.",
                total,
                limit
            );
            return;
        }

        var entities = await dbContext
            .Costs.AsNoTracking()
            .Include(x => x.Incoterms)
            .Include(x => x.Services)
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.CostType)
            .ThenBy(x => x.CarrierName)
            .ThenBy(x => x.AgentName)
            .ThenBy(x => x.PortName)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var costs = entities.Select(ToCostDto).ToList();

        var activeCosts = entities.Where(x => x.IsActive).Select(ToCostDto).ToList();

        var costsSelect = entities.Where(x => x.IsActive).Select(ToCostSelectDto).ToList();

        await costCache.SetCostsSelectAsync(costsSelect, cancellationToken: cancellationToken);

        foreach (var cost in costs)
        {
            await costCache.SetCostByIdAsync(cost.Id, cost, cancellationToken: cancellationToken);
        }

        await costCache.SetActiveCostsAsync(activeCosts, cancellationToken: cancellationToken);

        await WarmCostsByTypeAsync(activeCosts, cancellationToken);

        await WarmCostsByDetailTypeAsync(activeCosts, cancellationToken);

        await WarmCostsByCarrierAsync(activeCosts, cancellationToken);

        await WarmCostsByAgentAsync(activeCosts, cancellationToken);

        await WarmCostsByPortAsync(activeCosts, cancellationToken);

        await WarmCostsByCarrierAndPortAsync(activeCosts, cancellationToken);

        await WarmCostsByAgentAndPortAsync(activeCosts, cancellationToken);

        logger.LogInformation(
            "Pricing cost cache warmup completed. "
                + "Costs: {CostsCount}, "
                + "ActiveCosts: {ActiveCostsCount}.",
            costs.Count,
            activeCosts.Count
        );
    }

    private async Task WarmCostsByTypeAsync(
        IReadOnlyCollection<CostDto> costs,
        CancellationToken cancellationToken
    )
    {
        foreach (var group in costs.GroupBy(x => x.CostType))
        {
            var costType = Enum.Parse<Dhole.Pricing.Domain.Costs.Enums.CostType>(group.Key);

            await costCache.SetActiveCostsAsync(
                group.ToList(),
                costType: costType,
                cancellationToken: cancellationToken
            );
        }
    }

    private async Task WarmCostsByDetailTypeAsync(
        IReadOnlyCollection<CostDto> costs,
        CancellationToken cancellationToken
    )
    {
        foreach (var group in costs.GroupBy(x => x.CostDetailType))
        {
            var costDetailType = Enum.Parse<Dhole.Pricing.Domain.Costs.Enums.CostDetailType>(
                group.Key
            );

            await costCache.SetActiveCostsAsync(
                group.ToList(),
                costDetailType: costDetailType,
                cancellationToken: cancellationToken
            );
        }
    }

    private async Task WarmCostsByCarrierAsync(
        IReadOnlyCollection<CostDto> costs,
        CancellationToken cancellationToken
    )
    {
        var groups = costs.Where(x => x.CarrierId.HasValue).GroupBy(x => x.CarrierId!.Value);

        foreach (var group in groups)
        {
            await costCache.SetActiveCostsAsync(
                group.ToList(),
                carrierId: group.Key,
                cancellationToken: cancellationToken
            );
        }
    }

    private async Task WarmCostsByAgentAsync(
        IReadOnlyCollection<CostDto> costs,
        CancellationToken cancellationToken
    )
    {
        var groups = costs.Where(x => x.AgentId.HasValue).GroupBy(x => x.AgentId!.Value);

        foreach (var group in groups)
        {
            await costCache.SetActiveCostsAsync(
                group.ToList(),
                agentId: group.Key,
                cancellationToken: cancellationToken
            );
        }
    }

    private async Task WarmCostsByPortAsync(
        IReadOnlyCollection<CostDto> costs,
        CancellationToken cancellationToken
    )
    {
        var groups = costs
            .SelectMany(cost => RoutePortIds(cost).Select(portId => new { cost, portId }))
            .GroupBy(x => x.portId);

        foreach (var group in groups)
        {
            await costCache.SetActiveCostsAsync(
                group.Select(x => x.cost).DistinctBy(x => x.Id).ToList(),
                portId: group.Key,
                cancellationToken: cancellationToken
            );
        }
    }

    private async Task WarmCostsByCarrierAndPortAsync(
        IReadOnlyCollection<CostDto> costs,
        CancellationToken cancellationToken
    )
    {
        var groups = costs
            .Where(x => x.CarrierId.HasValue)
            .SelectMany(cost => RoutePortIds(cost).Select(portId => new { cost, portId }))
            .GroupBy(x => new { CarrierId = x.cost.CarrierId!.Value, x.portId });

        foreach (var group in groups)
        {
            await costCache.SetActiveCostsAsync(
                group.Select(x => x.cost).DistinctBy(x => x.Id).ToList(),
                carrierId: group.Key.CarrierId,
                portId: group.Key.portId,
                cancellationToken: cancellationToken
            );
        }
    }

    private async Task WarmCostsByAgentAndPortAsync(
        IReadOnlyCollection<CostDto> costs,
        CancellationToken cancellationToken
    )
    {
        var groups = costs
            .Where(x => x.AgentId.HasValue)
            .SelectMany(cost => RoutePortIds(cost).Select(portId => new { cost, portId }))
            .GroupBy(x => new { AgentId = x.cost.AgentId!.Value, x.portId });

        foreach (var group in groups)
        {
            await costCache.SetActiveCostsAsync(
                group.Select(x => x.cost).DistinctBy(x => x.Id).ToList(),
                agentId: group.Key.AgentId,
                portId: group.Key.portId,
                cancellationToken: cancellationToken
            );
        }
    }

    private static IEnumerable<Guid> RoutePortIds(CostDto cost)
    {
        if (cost.PortId.HasValue)
            yield return cost.PortId.Value;
        if (cost.PolId.HasValue)
            yield return cost.PolId.Value;
        if (cost.PoeId.HasValue)
            yield return cost.PoeId.Value;
        if (cost.PodId.HasValue)
            yield return cost.PodId.Value;
    }

    private async Task WarmImportRatesAsync(CancellationToken cancellationToken)
    {
        var total = await dbContext.ImportFclRates.AsNoTracking().CountAsync(x => !x.IsDeleted, cancellationToken);
        var limit = ReadPositiveInt("Pricing:CacheWarmup:MaxImportRates", 250);
        if (total > limit)
        {
            logger.LogWarning(
                "Se omite warmup de ImportFclRates: {Total} registros exceden el límite seguro {Limit}. El cache se cargará bajo demanda.",
                total,
                limit
            );
            return;
        }

        var entities = await dbContext
            .ImportFclRates.AsNoTracking()
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.Carrier)
            .ThenBy(x => x.Pol)
            .ThenBy(x => x.Pod)
            .ThenBy(x => x.ContainerType)
            .ThenBy(x => x.ValidFrom)
            .ToListAsync(cancellationToken);

        var imports = entities.Select(ToImportRateDto).ToList();

        await importRateCache.SetImportRatesAsync(imports, cancellationToken: cancellationToken);

        foreach (var import in imports)
        {
            await importRateCache.SetImportRateByIdAsync(
                import.Id,
                import,
                cancellationToken: cancellationToken
            );
        }

        foreach (var group in imports.GroupBy(x => x.ImportBatchId))
        {
            await importRateCache.SetImportRatesByBatchAsync(
                group.Key,
                group.ToList(),
                cancellationToken: cancellationToken
            );
        }

        foreach (var group in entities.GroupBy(x => x.Status))
        {
            await importRateCache.SetImportRatesAsync(
                group.Select(ToImportRateDto).ToList(),
                status: group.Key,
                cancellationToken: cancellationToken
            );
        }

        foreach (var group in entities.GroupBy(x => x.SourceType))
        {
            await importRateCache.SetImportRatesAsync(
                group.Select(ToImportRateDto).ToList(),
                sourceType: group.Key,
                cancellationToken: cancellationToken
            );
        }

        logger.LogInformation(
            "Pricing import rate cache warmup completed. "
                + "Imports: {ImportsCount}, "
                + "Batches: {BatchesCount}.",
            imports.Count,
            imports.Select(x => x.ImportBatchId).Distinct().Count()
        );
    }

    private async Task WarmRateHeadersAsync(CancellationToken cancellationToken)
    {
        var total = await dbContext.RateHeaders.AsNoTracking().CountAsync(x => !x.IsDeleted, cancellationToken);
        var limit = ReadPositiveInt("Pricing:CacheWarmup:MaxRateHeaders", 500);
        if (total > limit)
        {
            logger.LogWarning(
                "Se omite warmup de RateHeaders: {Total} registros exceden el límite seguro {Limit}. El cache se cargará bajo demanda.",
                total,
                limit
            );
            return;
        }

        var rateHeaders = await dbContext
            .RateHeaders.AsNoTracking()
            .Include(x => x.RateDetails)
            .Include(x => x.RateContainers)
            .Include(x => x.RateServices)
            .AsSplitQuery()
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.ValidFrom)
            .ThenBy(x => x.CarrierName)
            .ThenBy(x => x.PolName)
            .ThenBy(x => x.PodName)
            .ToListAsync(cancellationToken);

        var rateDtos = rateHeaders.Select(ToRateDto).ToList();

        var rateSelect = rateHeaders.Select(ToRateSelectDto).ToList();

        await rateHeaderCache.SetRateHeadersSelectAsync(
            rateSelect,
            cancellationToken: cancellationToken
        );

        foreach (var rate in rateDtos)
        {
            await rateHeaderCache.SetRateHeaderByIdAsync(
                rate.Id,
                rate,
                cancellationToken: cancellationToken
            );
        }

        var quoteDate = DateTime.UtcNow.Date;
        var nextDate = quoteDate.AddDays(1);

        var validRates = rateHeaders
            .Where(x =>
                x.Status == RateStatus.Open
                && x.ValidFrom < nextDate
                && x.ValidTo >= quoteDate
            )
            .Select(ToRateDto)
            .ToList();

        await rateHeaderCache.SetValidRateHeadersAsync(
            validRates,
            cancellationToken: cancellationToken
        );

        await rateHeaderCache.SetValidRateHeadersAsync(
            validRates,
            quoteDate: quoteDate,
            cancellationToken: cancellationToken
        );

        await WarmValidRatesByAgentAsync(validRates, quoteDate, cancellationToken);

        await WarmValidRatesByCarrierAsync(validRates, quoteDate, cancellationToken);

        await WarmValidRatesByRouteAsync(validRates, quoteDate, cancellationToken);

        logger.LogInformation(
            "Pricing rate header cache warmup completed. "
                + "Rates: {RatesCount}, "
                + "ValidRates: {ValidRatesCount}, "
                + "QuoteDate: {QuoteDate}.",
            rateDtos.Count,
            validRates.Count,
            quoteDate
        );
    }

    private async Task WarmValidRatesByAgentAsync(
        IReadOnlyCollection<RateDto> rates,
        DateTime quoteDate,
        CancellationToken cancellationToken
    )
    {
        foreach (var group in rates.GroupBy(x => x.AgentId))
        {
            await rateHeaderCache.SetValidRateHeadersAsync(
                group.ToList(),
                agentId: group.Key,
                quoteDate: quoteDate,
                cancellationToken: cancellationToken
            );
        }
    }

    private async Task WarmValidRatesByCarrierAsync(
        IReadOnlyCollection<RateDto> rates,
        DateTime quoteDate,
        CancellationToken cancellationToken
    )
    {
        foreach (var group in rates.GroupBy(x => x.CarrierId))
        {
            await rateHeaderCache.SetValidRateHeadersAsync(
                group.ToList(),
                carrierId: group.Key,
                quoteDate: quoteDate,
                cancellationToken: cancellationToken
            );
        }
    }

    private async Task WarmValidRatesByRouteAsync(
        IReadOnlyCollection<RateDto> rates,
        DateTime quoteDate,
        CancellationToken cancellationToken
    )
    {
        var groups = rates.GroupBy(x => new
        {
            x.AgentId,
            x.CarrierId,
            x.PolId,
            x.PoeId,
            x.PodId,
            x.ContainerTypeId,
            x.CurrencyId,
        });

        foreach (var group in groups)
        {
            await rateHeaderCache.SetValidRateHeadersAsync(
                group.ToList(),
                agentId: group.Key.AgentId,
                carrierId: group.Key.CarrierId,
                polId: group.Key.PolId,
                poeId: group.Key.PoeId,
                podId: group.Key.PodId,
                containerTypeId: group.Key.ContainerTypeId,
                currencyId: group.Key.CurrencyId,
                quoteDate: quoteDate,
                cancellationToken: cancellationToken
            );
        }
    }

    private int ReadPositiveInt(string key, int fallback)
    {
        var value = configuration[key];
        return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
    }

    private static CostDto ToCostDto(Dhole.Pricing.Domain.Costs.Entities.Cost cost)
    {
        return new CostDto(
            cost.Id,
            cost.Name,
            cost.CostType.ToString(),
            cost.CostDetailType.ToString(),
            cost.CarrierId,
            cost.CarrierName,
            cost.CarrierCode,
            cost.AgentId,
            cost.AgentName,
            cost.AgentCode,
            cost.PortId,
            cost.PortName,
            cost.PortCode,
            cost.PortRole.ToString(),
            cost.CurrencyId,
            cost.CurrencyName,
            cost.CurrencyCode,
            cost.CostAmount,
            cost.SaleAmount,
            cost.UtilityAmount,
            cost.Notes!,
            cost.IsAccountant,
            cost.IsActive,
            cost.Incoterms
                .OrderBy(x => x.IncotermName)
                .Select(x => new CostIncotermDto(x.IncotermId, x.IncotermName, x.IncotermCode))
                .ToArray(),
            cost.PolId,
            cost.PolName,
            cost.PolCode,
            cost.PoeId,
            cost.PoeName,
            cost.PoeCode,
            cost.PodId,
            cost.PodName,
            cost.PodCode,
            cost.ShipmentMode?.ToString(),
            cost.ChargeBasis.ToString(),
            cost.MinimumCostAmount,
            cost.MinimumSaleAmount,
            cost.KgPerCbm,
            cost.Services
                .OrderBy(x => x.ServiceName)
                .Select(x => new CostServiceDto(x.ServiceId, x.ServiceName, x.ServiceCode))
                .ToArray()
        );
    }

    private static CostSelectDto ToCostSelectDto(Dhole.Pricing.Domain.Costs.Entities.Cost cost)
    {
        return new CostSelectDto(
            cost.Id,
            cost.Name,
            cost.CostType.ToString(),
            cost.CostDetailType.ToString(),
            cost.CarrierId,
            cost.CarrierName,
            cost.CarrierCode,
            cost.AgentId,
            cost.AgentName,
            cost.AgentCode,
            cost.PortId,
            cost.PortName,
            cost.PortCode,
            cost.PortRole.ToString(),
            cost.CurrencyId,
            cost.CurrencyName,
            cost.CurrencyCode,
            cost.CostAmount,
            cost.SaleAmount,
            cost.UtilityAmount,
            cost.Notes!,
            cost.IsAccountant,
            cost.Incoterms
                .OrderBy(x => x.IncotermName)
                .Select(x => new CostIncotermDto(x.IncotermId, x.IncotermName, x.IncotermCode))
                .ToArray(),
            cost.PolId,
            cost.PolName,
            cost.PolCode,
            cost.PoeId,
            cost.PoeName,
            cost.PoeCode,
            cost.PodId,
            cost.PodName,
            cost.PodCode,
            cost.ShipmentMode?.ToString(),
            cost.ChargeBasis.ToString(),
            cost.MinimumCostAmount,
            cost.MinimumSaleAmount,
            cost.KgPerCbm,
            cost.Services
                .OrderBy(x => x.ServiceName)
                .Select(x => new CostServiceDto(x.ServiceId, x.ServiceName, x.ServiceCode))
                .ToArray()
        );
    }

    private static ImportRateDto ToImportRateDto(
        Dhole.Pricing.Domain.Imports.Entities.ImportFclRates import
    )
    {
        return new ImportRateDto
        {
            Id = import.Id,
            ImportBatchId = import.ImportBatchId,
            ExtractionRecordId = import.ExtractionRecordId,
            SourceType = import.SourceType.ToString(),
            ImportProfileId = import.ImportProfileId,
            ImportProfileName = import.ImportProfileName,
            ImportProfileCode = import.ImportProfileCode,
            ImportProfileSlug = import.ImportProfileSlug,
            PolId = import.PolId,
            Pol = import.PolName,
            PolCode = import.PolCode,
            PolSlug = import.PolSlug,
            PoeId = import.PoeId,
            Poe = import.PoeName,
            PoeCode = import.PoeCode,
            PoeSlug = import.PoeSlug,
            PodId = import.PodId,
            Pod = import.PodName,
            PodCode = import.PodCode,
            PodSlug = import.PodSlug,
            CarrierId = import.CarrierId,
            Carrier = import.CarrierName,
            CarrierCode = import.CarrierCode,
            CarrierSlug = import.CarrierSlug,
            AgentId = import.AgentId,
            Agent = import.AgentName,
            AgentCode = import.AgentCode,
            AgentSlug = import.AgentSlug,
            ContainerTypeId = import.ContainerTypeId,
            ContainerType = import.ContainerTypeName,
            ContainerTypeCode = import.ContainerTypeCode,
            ContainerTypeSlug = import.ContainerTypeSlug,
            CurrencyId = import.CurrencyId,
            Currency = import.CurrencyName,
            CurrencyCode = import.CurrencyCode,
            CurrencySlug = import.CurrencySlug,
            Commodity = import.Commodity,
            Freight = import.OceanFreight ?? import.Freight,
            OceanFreight = import.OceanFreight,
            OriginCharges = import.OriginCharges,
            DestinationCharges = import.DestinationCharges,
            Surcharges = import.Surcharges,
            TotalCost = import.TotalCost,
            TotalSale = import.TotalSale,
            Profit = import.Profit,
            Margin = import.Margin,
            FreeDays = import.FreeDays,
            TransitDays = import.TransitDays,
            ValidFrom = import.ValidFrom,
            ValidTo = import.ValidTo,
            RawDataJson = import.RawDataJson ?? "{}",
            Status = import.Status.ToString(),
            UsedAsRateCount = import.UsedAsRateCount,
            CreatedAsRateHeaderId = import.CreatedAsRateHeaderId,
        };
    }

    private static RateDto ToRateDto(RateHeader rate)
    {
        return new RateDto(
            rate.Id,
            rate.RateCode,
            rate.RateName,
            rate.RevisionNumber,
            rate.SourceImportFclRateId,
            rate.AgentId,
            rate.AgentName,
            rate.AgentCode,
            rate.CarrierId,
            rate.CarrierName,
            rate.CarrierCode,
            rate.PolId,
            rate.PolName,
            rate.PolCode,
            rate.PoeId,
            rate.PoeName,
            rate.PoeCode,
            rate.PodId,
            rate.PodName,
            rate.PodCode,
            rate.ContainerTypeId,
            rate.ContainerTypeName,
            rate.ContainerTypeCode,
            rate.IncotermId,
            rate.IncotermName,
            rate.IncotermCode,
            rate.PickupAddress,
            rate.PickupLatitude,
            rate.PickupLongitude,
            rate.ContainerQuantity,
            rate.CurrencyId,
            rate.CurrencyName,
            rate.CurrencyCode,
            rate.ExchangeRatePurchase,
            rate.ExchangeRateSale,
            rate.ExchangeRateApplied,
            rate.ExchangeRateDate,
            rate.ExchangeRateCapturedAtUtc,
            rate.ExchangeRateSource,
            rate.ExchangeRateManualOverride,
            rate.FreeDays,
            rate.ValidFrom,
            rate.ValidTo,
            rate.ClientName,
            rate.ExecutiveName,
            rate.IdtraNumber,
            rate.QuoNumber,
            rate.Includes,
            rate.SubjectTo,
            rate.Excludes,
            rate.TransitTime,
            rate.RateType.ToString(),
            rate.ShipmentMode.ToString(),
            rate.OperationType.ToString(),
            rate.TotalPackages,
            rate.TotalPallets,
            rate.TotalWeightKg,
            rate.TotalVolumeCbm,
            rate.KgPerCbm,
            rate.ChargeableQuantity,
            Array.Empty<RateCargoLineDto>(),
            rate.TotalCostAmount,
            rate.TotalSaleAmount,
            rate.TotalUtilityAmount,
            rate.TotalCostUsd,
            rate.TotalSaleUsd,
            rate.TotalUtilityUsd,
            rate.TotalCostCrc,
            rate.TotalSaleCrc,
            rate.TotalUtilityCrc,
            rate.MarginPercentage,
            rate.RequiredApproval,
            rate.Status.ToString(),
            rate.ClosedReason,
            rate.ClosedAtUtc,
            rate.ClosedBy,
            (rate.RateContainers.Count > 0
                ? rate.RateContainers
                    .OrderBy(x => x.ContainerTypeName)
                    .ThenBy(x => x.ContainerTypeCode)
                    .Select(x => new RateContainerDto(
                        x.Id,
                        x.RateHeaderId,
                        x.ContainerTypeId,
                        x.ContainerTypeName,
                        x.ContainerTypeCode,
                        x.Quantity
                    ))
                : new[]
                {
                    new RateContainerDto(
                        Guid.Empty,
                        rate.Id,
                        rate.ContainerTypeId,
                        rate.ContainerTypeName,
                        rate.ContainerTypeCode,
                        rate.ContainerQuantity
                    )
                })
                .ToList(),
            rate.RateDetails.OrderBy(x => x.CostType)
                .ThenBy(x => x.CostDetailType)
                .ThenBy(x => x.Name)
                .Select(ToRateDetailDto)
                .ToList(),
            rate.RateServices
                .OrderBy(x => x.ServiceName)
                .Select(x => new RateServiceDto(x.ServiceId, x.ServiceName, x.ServiceCode))
                .ToList()
        );
    }

    private static RateDetailDto ToRateDetailDto(RateDetail detail)
    {
        return new RateDetailDto(
            detail.Id,
            detail.RateHeaderId,
            detail.CostId,
            detail.Name,
            detail.CostDetailType.ToString(),
            detail.CostType.ToString(),
            detail.ChargeBasis.ToString(),
            detail.CurrencyId,
            detail.CurrencyName,
            detail.CurrencyCode,
            detail.CostAmount,
            detail.SaleAmount,
            detail.UtilityAmount,
            detail.Quantity,
            detail.Notes
        );
    }

    private static RateSelectDto ToRateSelectDto(RateHeader rate)
    {
        return new RateSelectDto(
            rate.Id,
            BuildRateLabel(rate),
            rate.Status.ToString(),
            rate.RequiredApproval
        );
    }

    private static string BuildRateLabel(RateHeader rate)
    {
        return $"{rate.AgentName} | "
            + $"{rate.CarrierName} | "
            + $"{rate.PolCode} → {rate.PodCode} | "
            + $"{rate.ContainerTypeCode} | "
            + $"{rate.CurrencyCode} {rate.TotalSaleAmount:N2}";
    }
}

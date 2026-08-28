from pathlib import Path


def read(path: str) -> str:
    return Path(path).read_text(encoding='utf-8')


def write(path: str, content: str) -> None:
    p = Path(path)
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(content, encoding='utf-8')


def replace_once(path: str, old: str, new: str) -> None:
    text = read(path)
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f'{path}: expected exactly one match, found {count}: {old[:120]!r}')
    write(path, text.replace(old, new, 1))


def replace_all(path: str, old: str, new: str, minimum: int = 1) -> None:
    text = read(path)
    count = text.count(old)
    if count < minimum:
        raise RuntimeError(f'{path}: expected at least {minimum} matches, found {count}: {old[:120]!r}')
    write(path, text.replace(old, new))

# -----------------------------------------------------------------------------
# Cost <-> pricing-services association
# -----------------------------------------------------------------------------
write('src/Dhole.Pricing.Domain/Costs/Entities/CostService.cs', '''namespace Dhole.Pricing.Domain.Costs.Entities;

public sealed class CostService
{
    private CostService() { }

    internal CostService(Guid costId, Guid serviceId, string serviceName, string serviceCode)
    {
        if (costId == Guid.Empty || serviceId == Guid.Empty)
            throw new InvalidOperationException("El costo y el servicio de Pricing son obligatorios.");
        if (string.IsNullOrWhiteSpace(serviceName) || string.IsNullOrWhiteSpace(serviceCode))
            throw new InvalidOperationException("El servicio de Pricing debe incluir nombre y código.");

        CostId = costId;
        ServiceId = serviceId;
        ServiceName = serviceName.Trim();
        ServiceCode = serviceCode.Trim();
    }

    internal void UpdateSnapshot(string serviceName, string serviceCode)
    {
        if (string.IsNullOrWhiteSpace(serviceName) || string.IsNullOrWhiteSpace(serviceCode))
            throw new InvalidOperationException("El servicio de Pricing debe incluir nombre y código.");
        ServiceName = serviceName.Trim();
        ServiceCode = serviceCode.Trim();
    }

    public Guid CostId { get; private set; }
    public Guid ServiceId { get; private set; }
    public string ServiceName { get; private set; } = string.Empty;
    public string ServiceCode { get; private set; } = string.Empty;
}

public sealed record CostServiceSelection(Guid Id, string Name, string Code);
''')

write('src/Dhole.Pricing.Persistence/Configurations/Costs/CostServiceConfiguration.cs', '''using Dhole.Pricing.Domain.Costs.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhole.Pricing.Persistence.Configurations.Costs;

internal sealed class CostServiceConfiguration : IEntityTypeConfiguration<CostService>
{
    public void Configure(EntityTypeBuilder<CostService> builder)
    {
        builder.ToTable("CostServices");
        builder.HasKey(x => new { x.CostId, x.ServiceId });
        builder.Property(x => x.CostId).IsRequired();
        builder.Property(x => x.ServiceId).IsRequired();
        builder.Property(x => x.ServiceName).HasMaxLength(160).IsRequired();
        builder.Property(x => x.ServiceCode).HasMaxLength(80).IsRequired();
        builder.HasIndex(x => x.ServiceId);
        builder.HasIndex(x => x.ServiceCode);
    }
}
''')

cost_path = 'src/Dhole.Pricing.Domain/Costs/Entities/Cost.cs'
replace_once(cost_path,
'''    private readonly List<CostIncoterm> _incoterms = [];
''',
'''    private readonly List<CostIncoterm> _incoterms = [];
    private readonly List<CostService> _services = [];
''')
replace_once(cost_path,
'''    public IReadOnlyCollection<CostIncoterm> Incoterms => _incoterms.AsReadOnly();
''',
'''    public IReadOnlyCollection<CostIncoterm> Incoterms => _incoterms.AsReadOnly();
    public IReadOnlyCollection<CostService> Services => _services.AsReadOnly();
''')
replace_once(cost_path,
'''    public void Delete(Guid? deletedBy)
''',
'''    public void ConfigureServices(IReadOnlyCollection<CostServiceSelection>? services)
    {
        var selections = services ?? [];
        var normalized = selections
            .Where(x => x.Id != Guid.Empty)
            .GroupBy(x => x.Id)
            .Select(group => group.First())
            .ToArray();

        if (normalized.Length != selections.Count)
            throw new InvalidOperationException("Los servicios de Pricing del costo no pueden estar vacíos ni repetidos.");

        var selectedIds = normalized.Select(x => x.Id).ToHashSet();
        _services.RemoveAll(x => !selectedIds.Contains(x.ServiceId));

        foreach (var service in normalized)
        {
            var existing = _services.FirstOrDefault(x => x.ServiceId == service.Id);
            if (existing is null)
            {
                _services.Add(new CostService(Id, service.Id, service.Name, service.Code));
                continue;
            }
            existing.UpdateSnapshot(service.Name, service.Code);
        }
    }

    public void Delete(Guid? deletedBy)
''')

cost_cfg = 'src/Dhole.Pricing.Persistence/Configurations/Costs/CostConfiguration.cs'
replace_once(cost_cfg,
'''        builder.HasMany(x => x.Incoterms)
            .WithOne()
            .HasForeignKey(x => x.CostId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Incoterms).UsePropertyAccessMode(PropertyAccessMode.Field);
''',
'''        builder.HasMany(x => x.Incoterms)
            .WithOne()
            .HasForeignKey(x => x.CostId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Incoterms).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(x => x.Services)
            .WithOne()
            .HasForeignKey(x => x.CostId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Services).UsePropertyAccessMode(PropertyAccessMode.Field);
''')

write('src/Dhole.Pricing.Contracts/Costs/Request/CostServiceRequest.cs', '''namespace Dhole.Pricing.Contracts.Costs.Request;

public sealed record CostServiceRequest(Guid Id, string Name, string Code);
''')
write('src/Dhole.Pricing.Contracts/Costs/Response/CostServiceDto.cs', '''namespace Dhole.Pricing.Contracts.Costs.Response;

public sealed record CostServiceDto(Guid Id, string Name, string Code);
''')

for request_path in [
    'src/Dhole.Pricing.Contracts/Costs/Request/CreateCostRequest.cs',
    'src/Dhole.Pricing.Contracts/Costs/Request/UpdateCostRequest.cs',
]:
    replace_once(request_path,
'''    decimal? KgPerCbm = null
);''',
'''    decimal? KgPerCbm = null,
    IReadOnlyCollection<CostServiceRequest>? Services = null
);''')

for dto_path in [
    'src/Dhole.Pricing.Contracts/Costs/Response/CostDto.cs',
    'src/Dhole.Pricing.Contracts/Costs/Response/CostSelectDto.cs',
]:
    replace_once(dto_path,
'''    decimal? KgPerCbm = null
);''',
'''    decimal? KgPerCbm = null,
    IReadOnlyCollection<CostServiceDto>? Services = null
);''')

for command_path in [
    'src/Dhole.Pricing.Application/Features/Costs/CreateCost/CreateCostCommand.cs',
    'src/Dhole.Pricing.Application/Features/Costs/UpdateCost/UpdateCostCommand.cs',
]:
    replace_once(command_path,
'''    IReadOnlyCollection<CostIncotermSelection> Incoterms,
    ShipmentMode? ShipmentMode,''',
'''    IReadOnlyCollection<CostIncotermSelection> Incoterms,
    IReadOnlyCollection<CostServiceSelection> Services,
    ShipmentMode? ShipmentMode,''')

for handler_path in [
    'src/Dhole.Pricing.Application/Features/Costs/CreateCost/CreateCostCommandHandler.cs',
    'src/Dhole.Pricing.Application/Features/Costs/UpdateCost/UpdateCostCommandHandler.cs',
]:
    replace_once(handler_path,
'''            command = command with
            {''',
'''            var normalizedServices = new List<CostServiceSelection>();
            foreach (var selected in command.Services ?? Array.Empty<CostServiceSelection>())
            {
                var service = await configCatalog.GetActiveInGroupAsync(
                    selected.Id, PricingConstants.CatalogSlugs.PricingServices, cancellationToken);
                if (service is null)
                    return Result.Failure'''+('<Guid>' if 'CreateCost' in handler_path else '')+'''(PricingErrors.InvalidConfigCatalogReference(
                        "El servicio de Pricing", PricingConstants.CatalogSlugs.PricingServices));
                normalizedServices.Add(new CostServiceSelection(
                    service.Id, service.SnapshotName(preferValue: true), service.Code));
            }

            command = command with
            {''')
    replace_once(handler_path,
'''                Incoterms = normalizedIncoterms,
''',
'''                Incoterms = normalizedIncoterms,
                Services = normalizedServices,
''')

# Configure services after create/update aggregate mutation.
replace_once('src/Dhole.Pricing.Application/Features/Costs/CreateCost/CreateCostCommandHandler.cs',
'''            cost = Cost.Create(
''',
'''            cost = Cost.Create(
''')
replace_once('src/Dhole.Pricing.Application/Features/Costs/CreateCost/CreateCostCommandHandler.cs',
'''                command.CreatedBy
            );
        }
''',
'''                command.CreatedBy
            );
            cost.ConfigureServices(command.Services);
        }
''')
replace_once('src/Dhole.Pricing.Application/Features/Costs/UpdateCost/UpdateCostCommandHandler.cs',
'''                command.UpdatedBy
            );
        }
''',
'''                command.UpdatedBy
            );
            cost.ConfigureServices(command.Services);
        }
''')

cost_endpoints = 'src/Dhole.Pricing.Api/Endpoints/CostEndpoints.cs'
replace_all(cost_endpoints,
'''                (request.Incoterms ?? [])
                    .Select(x => new CostIncotermSelection(x.Id, x.Name, x.Code))
                    .ToArray(),
                shipmentMode,''',
'''                (request.Incoterms ?? [])
                    .Select(x => new CostIncotermSelection(x.Id, x.Name, x.Code))
                    .ToArray(),
                (request.Services ?? [])
                    .Select(x => new CostServiceSelection(x.Id, x.Name, x.Code))
                    .ToArray(),
                shipmentMode,''', minimum=2)
replace_once(cost_endpoints,
'''        bool? applicableToContext,
        IQueryDispatcher dispatcher,''',
'''        bool? applicableToContext,
        string? serviceIds,
        IQueryDispatcher dispatcher,''')
replace_once(cost_endpoints,
'''                shipmentMode,
                applicableToContext ?? false
''',
'''                shipmentMode,
                applicableToContext ?? false,
                ParseGuidList(serviceIds)
''')
replace_once(cost_endpoints,
'''    private static bool TryParseDefinedEnum<TEnum>(string? value, out TEnum result)
''',
'''    private static IReadOnlyCollection<Guid> ParseGuidList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Array.Empty<Guid>();
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(raw => Guid.TryParse(raw, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();
    }

    private static bool TryParseDefinedEnum<TEnum>(string? value, out TEnum result)
''')

query_path = 'src/Dhole.Pricing.Application/Features/Costs/GetCostsSelect/GetCostsForSelectQuery.cs'
replace_once(query_path,
'''    ShipmentMode? ShipmentMode = null,
    bool ApplicableToContext = false
''',
'''    ShipmentMode? ShipmentMode = null,
    bool ApplicableToContext = false,
    IReadOnlyCollection<Guid>? ServiceIds = null
''')

handler = 'src/Dhole.Pricing.Application/Features/Costs/GetCostsSelect/GetCostsForSelectQueryHandler.cs'
replace_once(handler,
'''        if (
            query.ShipmentMode.HasValue
''',
'''        if (cost.Services?.Count > 0)
        {
            if (query.ServiceIds is null || query.ServiceIds.Count == 0)
                return false;
            if (!cost.Services.Any(service => query.ServiceIds.Contains(service.Id)))
                return false;
        }

        if (
            query.ShipmentMode.HasValue
''')
replace_once(handler,
'''        if (cost.Incoterms.Count > 0) score += 2;
''',
'''        if (cost.Incoterms.Count > 0) score += 2;
        if (cost.Services?.Count > 0) score += 2;
''')
replace_once(handler,
'''            && !query.ShipmentMode.HasValue
            && query.IsActive == true;''',
'''            && !query.ShipmentMode.HasValue
            && (query.ServiceIds is null || query.ServiceIds.Count == 0)
            && query.IsActive == true;''')

repo = 'src/Dhole.Pricing.Persistence/Repositories/CostRepository.cs'
replace_once(repo,
'''        .Include(x => x.Incoterms)
        .FirstOrDefaultAsync''',
'''        .Include(x => x.Incoterms)
        .Include(x => x.Services)
        .FirstOrDefaultAsync''')
replace_once(repo,
'''dbContext.Costs.AsNoTracking().Include(x => x.Incoterms).Where''',
'''dbContext.Costs.AsNoTracking().Include(x => x.Incoterms).Include(x => x.Services).Where''')
# Append Services argument to both CostDto and CostSelectDto projections and GetById DTO.
replace_all(repo,
'''                x.MinimumSaleAmount,
                x.KgPerCbm
            ))''',
'''                x.MinimumSaleAmount,
                x.KgPerCbm,
                x.Services
                    .OrderBy(s => s.ServiceName)
                    .Select(s => new CostServiceDto(s.ServiceId, s.ServiceName, s.ServiceCode))
                    .ToList()
            ))''', minimum=2)
# Search costs by associated service too.
replace_once(repo,
'''                || x.Incoterms.Any(i => i.IncotermName.ToLower().Contains(value) || i.IncotermCode.ToLower().Contains(value))
                || x.CurrencyName''',
'''                || x.Incoterms.Any(i => i.IncotermName.ToLower().Contains(value) || i.IncotermCode.ToLower().Contains(value))
                || x.Services.Any(s => s.ServiceName.ToLower().Contains(value) || s.ServiceCode.ToLower().Contains(value))
                || x.CurrencyName''')

get_by_id = 'src/Dhole.Pricing.Application/Features/Costs/GetCostById/GetCostByIdQueryHandler.cs'
replace_once(get_by_id,
'''            cost.MinimumSaleAmount,
            cost.KgPerCbm
        );''',
'''            cost.MinimumSaleAmount,
            cost.KgPerCbm,
            cost.Services
                .OrderBy(x => x.ServiceName)
                .Select(x => new CostServiceDto(x.ServiceId, x.ServiceName, x.ServiceCode))
                .ToArray()
        );''')

# -----------------------------------------------------------------------------
# Rate operation, selected services and dual-currency totals
# -----------------------------------------------------------------------------
write('src/Dhole.Pricing.Domain/Rates/Enums/RateOperationType.cs', '''namespace Dhole.Pricing.Domain.Rates.Enums;

public enum RateOperationType
{
    Import = 1,
    Export = 2,
    TransitDomestic = 3,
}
''')
write('src/Dhole.Pricing.Domain/Rates/Entities/RateService.cs', '''namespace Dhole.Pricing.Domain.Rates.Entities;

public sealed class RateService
{
    private RateService() { }

    internal RateService(Guid rateHeaderId, Guid serviceId, string serviceName, string serviceCode)
    {
        if (rateHeaderId == Guid.Empty || serviceId == Guid.Empty)
            throw new InvalidOperationException("La tarifa y el servicio son obligatorios.");
        if (string.IsNullOrWhiteSpace(serviceName) || string.IsNullOrWhiteSpace(serviceCode))
            throw new InvalidOperationException("El servicio debe incluir nombre y código.");
        RateHeaderId = rateHeaderId;
        ServiceId = serviceId;
        ServiceName = serviceName.Trim();
        ServiceCode = serviceCode.Trim();
    }

    public Guid RateHeaderId { get; private set; }
    public Guid ServiceId { get; private set; }
    public string ServiceName { get; private set; } = string.Empty;
    public string ServiceCode { get; private set; } = string.Empty;
}

public sealed record RateServiceSelection(Guid Id, string Name, string Code);
''')
write('src/Dhole.Pricing.Persistence/Configurations/Rates/RateServiceConfiguration.cs', '''using Dhole.Pricing.Domain.Rates.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhole.Pricing.Persistence.Configurations.Rates;

internal sealed class RateServiceConfiguration : IEntityTypeConfiguration<RateService>
{
    public void Configure(EntityTypeBuilder<RateService> builder)
    {
        builder.ToTable("RateServices");
        builder.HasKey(x => new { x.RateHeaderId, x.ServiceId });
        builder.Property(x => x.ServiceName).HasMaxLength(160).IsRequired();
        builder.Property(x => x.ServiceCode).HasMaxLength(80).IsRequired();
        builder.HasIndex(x => x.ServiceId);
        builder.HasIndex(x => x.ServiceCode);
    }
}
''')

rate = 'src/Dhole.Pricing.Domain/Rates/Entities/RateHeader.cs'
replace_once(rate,
'''    private readonly List<RateDetail> _rateDetails = [];
''',
'''    private readonly List<RateDetail> _rateDetails = [];
    private readonly List<RateService> _rateServices = [];
''')
replace_once(rate,
'''    public IReadOnlyCollection<RateDetail> RateDetails => _rateDetails.AsReadOnly();
''',
'''    public IReadOnlyCollection<RateDetail> RateDetails => _rateDetails.AsReadOnly();
    public IReadOnlyCollection<RateService> RateServices => _rateServices.AsReadOnly();
''')
# Add properties near shipment/rate type.
replace_once(rate,
'''    public RateType RateType { get; private set; } = RateType.Tariff;
    public ShipmentMode ShipmentMode { get; private set; } = ShipmentMode.Fcl;
''',
'''    public RateType RateType { get; private set; } = RateType.Tariff;
    public ShipmentMode ShipmentMode { get; private set; } = ShipmentMode.Fcl;
    public RateOperationType OperationType { get; private set; } = RateOperationType.TransitDomestic;
''')
replace_once(rate,
'''    public decimal TotalCostAmount { get; private set; }
    public decimal TotalSaleAmount { get; private set; }
    public decimal TotalUtilityAmount { get; private set; }
''',
'''    public decimal TotalCostAmount { get; private set; }
    public decimal TotalSaleAmount { get; private set; }
    public decimal TotalUtilityAmount { get; private set; }
    public decimal TotalCostUsd { get; private set; }
    public decimal TotalSaleUsd { get; private set; }
    public decimal TotalUtilityUsd { get; private set; }
    public decimal TotalCostCrc { get; private set; }
    public decimal TotalSaleCrc { get; private set; }
    public decimal TotalUtilityCrc { get; private set; }
''')
# Add configuration methods before SetAmounts.
replace_once(rate,
'''    public void SetAmounts(Guid? updatedBy = null)
    {''',
'''    public void SetOperationType(RateOperationType operationType, Guid? updatedBy = null)
    {
        OperationType = operationType;
        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());
    }

    public void ConfigureServices(IReadOnlyCollection<RateServiceSelection>? services, Guid? updatedBy = null)
    {
        var selections = services ?? [];
        var normalized = selections.Where(x => x.Id != Guid.Empty).GroupBy(x => x.Id).Select(x => x.First()).ToArray();
        if (normalized.Length != selections.Count)
            throw new InvalidOperationException("Los servicios de la tarifa no pueden estar vacíos ni repetidos.");
        _rateServices.Clear();
        foreach (var service in normalized)
            _rateServices.Add(new RateService(Id, service.Id, service.Name, service.Code));
        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());
    }

    public void SetAmounts(Guid? updatedBy = null)
    {''')
# Replace old SetAmounts math block. This pattern exists once inside SetAmounts.
replace_once(rate,
'''        TotalCostAmount = _rateDetails.Sum(x => x.CostAmount * x.Quantity);
        TotalSaleAmount = _rateDetails.Sum(x => x.SaleAmount * x.Quantity);
        TotalUtilityAmount = TotalSaleAmount - TotalCostAmount;
        MarginPercentage = TotalSaleAmount <= 0m ? 0m : (TotalUtilityAmount / TotalSaleAmount) * 100m;
''',
'''        var exchangeRate = ExchangeRateApplied is > 0m ? ExchangeRateApplied.Value : ExchangeRateSale;
        decimal costUsd = 0m, saleUsd = 0m, costCrc = 0m, saleCrc = 0m;
        foreach (var detail in _rateDetails)
        {
            var cost = detail.CostAmount * detail.Quantity;
            var sale = detail.SaleAmount * detail.Quantity;
            var code = detail.CurrencyCode.Trim().ToUpperInvariant();
            if (code == "USD")
            {
                costUsd += cost;
                saleUsd += sale;
                if (exchangeRate is > 0m) { costCrc += cost * exchangeRate.Value; saleCrc += sale * exchangeRate.Value; }
            }
            else if (code == "CRC")
            {
                costCrc += cost;
                saleCrc += sale;
                if (exchangeRate is > 0m) { costUsd += cost / exchangeRate.Value; saleUsd += sale / exchangeRate.Value; }
            }
            else
            {
                // Backward compatibility for currencies outside the current USD/CRC conversion scope.
                if (string.Equals(CurrencyCode, code, StringComparison.OrdinalIgnoreCase))
                {
                    TotalCostAmount += cost;
                    TotalSaleAmount += sale;
                }
            }
        }
        TotalCostUsd = decimal.Round(costUsd, 2, MidpointRounding.AwayFromZero);
        TotalSaleUsd = decimal.Round(saleUsd, 2, MidpointRounding.AwayFromZero);
        TotalUtilityUsd = TotalSaleUsd - TotalCostUsd;
        TotalCostCrc = decimal.Round(costCrc, 2, MidpointRounding.AwayFromZero);
        TotalSaleCrc = decimal.Round(saleCrc, 2, MidpointRounding.AwayFromZero);
        TotalUtilityCrc = TotalSaleCrc - TotalCostCrc;

        if (string.Equals(CurrencyCode, "CRC", StringComparison.OrdinalIgnoreCase))
        {
            TotalCostAmount = TotalCostCrc;
            TotalSaleAmount = TotalSaleCrc;
            TotalUtilityAmount = TotalUtilityCrc;
        }
        else if (string.Equals(CurrencyCode, "USD", StringComparison.OrdinalIgnoreCase))
        {
            TotalCostAmount = TotalCostUsd;
            TotalSaleAmount = TotalSaleUsd;
            TotalUtilityAmount = TotalUtilityUsd;
        }
        else
        {
            TotalUtilityAmount = TotalSaleAmount - TotalCostAmount;
        }
        MarginPercentage = TotalSaleAmount <= 0m ? 0m : (TotalUtilityAmount / TotalSaleAmount) * 100m;
''')
# Ensure totals are reset before conversion loop.
replace_once(rate,
'''        var exchangeRate = ExchangeRateApplied is > 0m ? ExchangeRateApplied.Value : ExchangeRateSale;
''',
'''        TotalCostAmount = 0m;
        TotalSaleAmount = 0m;
        TotalUtilityAmount = 0m;
        var exchangeRate = ExchangeRateApplied is > 0m ? ExchangeRateApplied.Value : ExchangeRateSale;
''')

rate_cfg = 'src/Dhole.Pricing.Persistence/Configurations/Rates/RateHeaderConfiguration.cs'
replace_once(rate_cfg,
'''        builder.Property(x => x.ShipmentMode).HasConversion<string>().HasMaxLength(20).IsRequired();
''',
'''        builder.Property(x => x.ShipmentMode).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.OperationType).HasConversion<string>().HasMaxLength(30).IsRequired();
''')
replace_once(rate_cfg,
'''        builder.Property(x => x.TotalUtilityAmount).HasPrecision(18, 2).IsRequired();
''',
'''        builder.Property(x => x.TotalUtilityAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.TotalCostUsd).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.TotalSaleUsd).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.TotalUtilityUsd).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.TotalCostCrc).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.TotalSaleCrc).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.TotalUtilityCrc).HasPrecision(18, 2).IsRequired();
''')
replace_once(rate_cfg,
'''        builder.HasMany(x => x.RateDetails)
''',
'''        builder.HasMany(x => x.RateServices)
            .WithOne()
            .HasForeignKey(x => x.RateHeaderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.RateServices).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(x => x.RateDetails)
''')

write('src/Dhole.Pricing.Contracts/Rates/Request/RateServiceRequest.cs', '''namespace Dhole.Pricing.Contracts.Rates.Request;

public sealed record RateServiceRequest(Guid Id, string Name, string Code);
''')
write('src/Dhole.Pricing.Contracts/Rates/Response/RateServiceDto.cs', '''namespace Dhole.Pricing.Contracts.Rates.Response;

public sealed record RateServiceDto(Guid Id, string Name, string Code);
''')

for req in [
    'src/Dhole.Pricing.Contracts/Rates/Request/CreateRateRequest.cs',
    'src/Dhole.Pricing.Contracts/Rates/Request/UpdateRateRequest.cs',
]:
    # Append after final parameter in a way specific to files.
    if 'CreateRateRequest' in req:
        replace_once(req,
'''    decimal? ExchangeRateApplied = null
);''',
'''    decimal? ExchangeRateApplied = null,
    string OperationType = "TransitDomestic",
    IReadOnlyCollection<RateServiceRequest>? Services = null
);''')
    else:
        replace_once(req,
'''    decimal? PickupLongitude = null
);''',
'''    decimal? PickupLongitude = null,
    string OperationType = "TransitDomestic",
    IReadOnlyCollection<RateServiceRequest>? Services = null
);''')

# Commands: selected services and operation type are backend-normalized.
for cmd in [
    'src/Dhole.Pricing.Application/Features/Rates/CreateRate/CreateRateCommand.cs',
    'src/Dhole.Pricing.Application/Features/Rates/UpdateRate/UpdateRateCommand.cs',
]:
    replace_once(cmd,
'''    bool CanApproveLowMargin,
''',
'''    RateOperationType OperationType,
    IReadOnlyCollection<RateServiceSelection> Services,
    bool CanApproveLowMargin,
''') if 'CreateRate' in cmd else replace_once(cmd,
'''    Guid? UpdatedBy
''',
'''    RateOperationType OperationType,
    IReadOnlyCollection<RateServiceSelection> Services,
    Guid? UpdatedBy
''')

# Rate DTO adds persisted context and both currency totals.
rate_dto = 'src/Dhole.Pricing.Contracts/Rates/Response/RateDto.cs'
replace_once(rate_dto,
'''    string ShipmentMode,
''',
'''    string ShipmentMode,
    string OperationType,
''')
replace_once(rate_dto,
'''    decimal TotalUtilityAmount,
    decimal MarginPercentage,
''',
'''    decimal TotalUtilityAmount,
    decimal TotalCostUsd,
    decimal TotalSaleUsd,
    decimal TotalUtilityUsd,
    decimal TotalCostCrc,
    decimal TotalSaleCrc,
    decimal TotalUtilityCrc,
    decimal MarginPercentage,
''')
replace_once(rate_dto,
'''    IReadOnlyCollection<RateContainerDto> Containers,
    IReadOnlyCollection<RateDetailDto> RateDetails
''',
'''    IReadOnlyCollection<RateContainerDto> Containers,
    IReadOnlyCollection<RateDetailDto> RateDetails,
    IReadOnlyCollection<RateServiceDto> Services
''')

mapping = 'src/Dhole.Pricing.Application/Features/Rates/RateMappings.cs'
replace_once(mapping,
'''            rate.ShipmentMode.ToString(),
            rate.TotalPackages,''',
'''            rate.ShipmentMode.ToString(),
            rate.OperationType.ToString(),
            rate.TotalPackages,''')
replace_once(mapping,
'''            rate.TotalUtilityAmount,
            rate.MarginPercentage,''',
'''            rate.TotalUtilityAmount,
            rate.TotalCostUsd,
            rate.TotalSaleUsd,
            rate.TotalUtilityUsd,
            rate.TotalCostCrc,
            rate.TotalSaleCrc,
            rate.TotalUtilityCrc,
            rate.MarginPercentage,''')
replace_once(mapping,
'''                .ToList()
        );''',
'''                .ToList(),
            rate.RateServices
                .OrderBy(x => x.ServiceName)
                .Select(x => new RateServiceDto(x.ServiceId, x.ServiceName, x.ServiceCode))
                .ToList()
        );''')

# Create handler validates and snapshots selected services then saves operation + services.
create_handler = 'src/Dhole.Pricing.Application/Features/Rates/CreateRate/CreateRateCommandHandler.cs'
replace_once(create_handler,
'''            var normalizedContainers = new List<RateContainerCommandItem>();
''',
'''            var normalizedServices = new List<RateServiceSelection>();
            foreach (var selected in command.Services ?? Array.Empty<RateServiceSelection>())
            {
                var service = await configCatalog.GetActiveInGroupAsync(
                    selected.Id, PricingConstants.CatalogSlugs.PricingServices, cancellationToken);
                if (service is null)
                    return Result.Failure<Guid>(PricingErrors.InvalidConfigCatalogReference(
                        "El servicio de Pricing", PricingConstants.CatalogSlugs.PricingServices));
                normalizedServices.Add(new RateServiceSelection(
                    service.Id, service.SnapshotName(preferValue: true), service.Code));
            }

            var normalizedContainers = new List<RateContainerCommandItem>();
''')
replace_once(create_handler,
'''                CurrencyCode = currency.Code,
            };''',
'''                CurrencyCode = currency.Code,
                Services = normalizedServices,
            };''')
replace_once(create_handler,
'''            rate.ConfigureExecutive(command.ExecutiveName);
''',
'''            rate.ConfigureExecutive(command.ExecutiveName);
            rate.SetOperationType(command.OperationType, command.CreatedBy);
            rate.ConfigureServices(command.Services, command.CreatedBy);
''')

# Update handler gets the same normalized service validation and persists both fields.
update_handler = 'src/Dhole.Pricing.Application/Features/Rates/UpdateRate/UpdateRateCommandHandler.cs'
# insert before normalizedContainers in update handler if present
replace_once(update_handler,
'''            var normalizedContainers = new List<RateContainerCommandItem>();
''',
'''            var normalizedServices = new List<RateServiceSelection>();
            foreach (var selected in command.Services ?? Array.Empty<RateServiceSelection>())
            {
                var service = await configCatalog.GetActiveInGroupAsync(
                    selected.Id, PricingConstants.CatalogSlugs.PricingServices, cancellationToken);
                if (service is null)
                    return Result.Failure(PricingErrors.InvalidConfigCatalogReference(
                        "El servicio de Pricing", PricingConstants.CatalogSlugs.PricingServices));
                normalizedServices.Add(new RateServiceSelection(
                    service.Id, service.SnapshotName(preferValue: true), service.Code));
            }

            var normalizedContainers = new List<RateContainerCommandItem>();
''')
replace_once(update_handler,
'''                CurrencyCode = currency.Code,
            };''',
'''                CurrencyCode = currency.Code,
                Services = normalizedServices,
            };''')
# Put setters after exchange/config executive occurrence.
replace_once(update_handler,
'''            rate.ConfigureExecutive(command.ExecutiveName);
''',
'''            rate.ConfigureExecutive(command.ExecutiveName);
            rate.SetOperationType(command.OperationType, command.UpdatedBy);
            rate.ConfigureServices(command.Services, command.UpdatedBy);
''')

# API endpoint parses operation and maps services into commands.
rate_ep = 'src/Dhole.Pricing.Api/Endpoints/RateEndpoints.cs'
replace_once(rate_ep,
'''        if (!TryParseDefinedEnum(request.RateType, out RateType rateType))
''',
'''        if (!TryParseDefinedEnum(request.OperationType, out RateOperationType operationType))
        {
            return EndpointResults.BadRequest(
                "Pricing.InvalidOperationType",
                $"El tipo de operación '{request.OperationType}' no es válido.",
                httpContext
            );
        }

        if (!TryParseDefinedEnum(request.RateType, out RateType rateType))
''')
# In update method there is another shipment/rate type parse. Add operation before rate type by replacing second occurrence too.
# Use marker within UpdateRateAsync if operation parse not yet present there.
update_marker = '''        if (!TryParseDefinedEnum(request.RateType, out RateType rateType))'''
text = read(rate_ep)
positions = [i for i in range(len(text)) if text.startswith(update_marker, i)]
if len(positions) == 2:
    pos = positions[1]
    insertion = '''        if (!TryParseDefinedEnum(request.OperationType, out RateOperationType operationType))\n        {\n            return EndpointResults.BadRequest(\n                "Pricing.InvalidOperationType",\n                $"El tipo de operación '{request.OperationType}' no es válido.",\n                httpContext\n            );\n        }\n\n'''
    text = text[:pos] + insertion + text[pos:]
    write(rate_ep, text)
# Map create command right before CanApproveLowMargin.
replace_once(rate_ep,
'''                request.ExchangeRateApplied,
                canApproveImportedRate,
                canApproveLowMargin,''',
'''                request.ExchangeRateApplied,
                canApproveImportedRate,
                operationType,
                (request.Services ?? [])
                    .Select(x => new RateServiceSelection(x.Id, x.Name, x.Code))
                    .ToArray(),
                canApproveLowMargin,''')
# Map update command before current user ID using exact tail once after finding request pickup coordinates.
replace_once(rate_ep,
'''                request.PickupLongitude,
                httpContext.GetCurrentUserId()
''',
'''                request.PickupLongitude,
                operationType,
                (request.Services ?? [])
                    .Select(x => new RateServiceSelection(x.Id, x.Name, x.Code))
                    .ToArray(),
                httpContext.GetCurrentUserId()
''')

# -----------------------------------------------------------------------------
# Basic domain regression tests
# -----------------------------------------------------------------------------
write('tests/Dhole.Pricing.UnitTests/RateMixedCurrencyTests.cs', '''using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.UnitTests;

[TestClass]
public sealed class RateMixedCurrencyTests
{
    [TestMethod]
    public void SetAmounts_WithUsdAndCrc_ProducesEquivalentTotalsInBothCurrencies()
    {
        var rate = RateHeader.Create(
            "QUO-TEST", null, null, null, null, null, null, null,
            Guid.NewGuid(), "Shanghai", "CNSHA", Guid.NewGuid(), "Caldera, Costa Rica", "CRCAL",
            null, null, null, Guid.NewGuid(), "40 HC", "40HC", null, null, null,
            Guid.NewGuid(), "USD", "USD", 0, DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(30),
            1, null, null, null, null, null, null, null, null, RateType.Spot, null);
        rate.ConfigureExchangeRateSnapshot(500m, 510m, 510m, DateTime.UtcNow.Date, DateTime.UtcNow, "Test", false, null);
        rate.AddRateDetail(rate.Id, null, "Freight", CostDetailType.Freight, CostType.Fixed, ChargeBasis.PerShipment,
            Guid.NewGuid(), "USD", "USD", 100m, 150m, null, 1m, null);
        rate.AddRateDetail(rate.Id, null, "Aduanas", CostDetailType.CustomsCharge, CostType.Fixed, ChargeBasis.PerShipment,
            Guid.NewGuid(), "CRC", "CRC", 51000m, 76500m, null, 1m, null);

        rate.SetAmounts();

        Assert.AreEqual(200m, rate.TotalCostUsd);
        Assert.AreEqual(300m, rate.TotalSaleUsd);
        Assert.AreEqual(102000m, rate.TotalCostCrc);
        Assert.AreEqual(153000m, rate.TotalSaleCrc);
        Assert.AreEqual(300m, rate.TotalSaleAmount);
    }
}
''')

print('Pricing service/currency patch applied.')

from pathlib import Path

ROOT = Path('.')

def read(path): return (ROOT/path).read_text(encoding='utf-8')
def write(path, content):
    p=ROOT/path; p.parent.mkdir(parents=True, exist_ok=True); p.write_text(content, encoding='utf-8')
def replace(path, old, new, count=1):
    text=read(path); found=text.count(old)
    if found != count: raise RuntimeError(f'{path}: expected {count} matches, found {found}: {old[:120]!r}')
    write(path, text.replace(old,new,count))

# Domain revision entity.
write('src/Dhole.Pricing.Domain/Rates/Entities/RateRevision.cs', '''namespace Dhole.Pricing.Domain.Rates.Entities;

public sealed class RateRevision
{
    private RateRevision() { }

    private RateRevision(Guid id, Guid rateHeaderId, int revisionNumber, string status, string rateName,
        string? idtraNumber, string? quoNumber, decimal totalSaleUsd, decimal totalSaleCrc,
        decimal marginPercentage, string snapshotJson, Guid? createdBy)
    {
        Id = id;
        RateHeaderId = rateHeaderId;
        RevisionNumber = revisionNumber;
        Status = status.Trim();
        RateName = rateName.Trim();
        IdtraNumber = string.IsNullOrWhiteSpace(idtraNumber) ? null : idtraNumber.Trim();
        QuoNumber = string.IsNullOrWhiteSpace(quoNumber) ? null : quoNumber.Trim();
        TotalSaleUsd = totalSaleUsd;
        TotalSaleCrc = totalSaleCrc;
        MarginPercentage = marginPercentage;
        SnapshotJson = snapshotJson;
        CreatedAtUtc = DateTime.UtcNow;
        CreatedBy = createdBy;
    }

    public Guid Id { get; private set; }
    public Guid RateHeaderId { get; private set; }
    public int RevisionNumber { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public string RateName { get; private set; } = string.Empty;
    public string? IdtraNumber { get; private set; }
    public string? QuoNumber { get; private set; }
    public decimal TotalSaleUsd { get; private set; }
    public decimal TotalSaleCrc { get; private set; }
    public decimal MarginPercentage { get; private set; }
    public string SnapshotJson { get; private set; } = "{}";
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }

    public static RateRevision Create(Guid rateHeaderId, int revisionNumber, string status, string rateName,
        string? idtraNumber, string? quoNumber, decimal totalSaleUsd, decimal totalSaleCrc,
        decimal marginPercentage, string snapshotJson, Guid? createdBy)
    {
        if (rateHeaderId == Guid.Empty || revisionNumber < 1) throw new InvalidOperationException("La revisión de tarifa no es válida.");
        if (string.IsNullOrWhiteSpace(status) || string.IsNullOrWhiteSpace(rateName) || string.IsNullOrWhiteSpace(snapshotJson))
            throw new InvalidOperationException("La revisión requiere estado, nombre e instantánea.");
        return new RateRevision(Guid.NewGuid(), rateHeaderId, revisionNumber, status, rateName, idtraNumber,
            quoNumber, totalSaleUsd, totalSaleCrc, marginPercentage, snapshotJson, createdBy);
    }
}
''')

# Add revision number and controlled revision transition to current aggregate.
replace('src/Dhole.Pricing.Domain/Rates/Entities/RateHeader.cs',
'''    public string RateCode { get; private set; } = string.Empty;\n    public string RateName { get; private set; } = string.Empty;\n    public int ContainerQuantity { get; private set; }''',
'''    public string RateCode { get; private set; } = string.Empty;\n    public string RateName { get; private set; } = string.Empty;\n    public int RevisionNumber { get; private set; } = 1;\n    public int ContainerQuantity { get; private set; }''')
replace('src/Dhole.Pricing.Domain/Rates/Entities/RateHeader.cs',
'''    public void ConfigurePickupLocation(\n''',
'''    public void BeginRevision(Guid? updatedBy)\n    {\n        if (Status != RateStatus.AcceptedByClient)\n            throw new InvalidOperationException("Solo una tarifa aceptada puede iniciar una nueva revisión.");\n\n        RevisionNumber = Math.Max(1, RevisionNumber) + 1;\n        RequiredApproval = MarginPercentage < MinimumMarginPercentage;\n        Status = RequiredApproval ? RateStatus.PendingApproval : RateStatus.Open;\n        ClosedReason = null;\n        ClosedAtUtc = null;\n        ClosedBy = null;\n        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());\n        AddDomainEvent(new RateHeaderUpdatedDomainEvent(Id, updatedBy));\n    }\n\n    public void ConfigurePickupLocation(\n''')

# Persistence.
write('src/Dhole.Pricing.Persistence/Configurations/Rates/RateRevisionConfiguration.cs', '''using Dhole.Pricing.Domain.Rates.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhole.Pricing.Persistence.Configurations.Rates;

internal sealed class RateRevisionConfiguration : IEntityTypeConfiguration<RateRevision>
{
    public void Configure(EntityTypeBuilder<RateRevision> builder)
    {
        builder.ToTable("RateRevisions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.RateHeaderId).IsRequired();
        builder.Property(x => x.RevisionNumber).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();
        builder.Property(x => x.RateName).HasMaxLength(500).IsRequired();
        builder.Property(x => x.IdtraNumber).HasMaxLength(100).IsRequired(false);
        builder.Property(x => x.QuoNumber).HasMaxLength(100).IsRequired(false);
        builder.Property(x => x.TotalSaleUsd).HasPrecision(18,2).IsRequired();
        builder.Property(x => x.TotalSaleCrc).HasPrecision(18,2).IsRequired();
        builder.Property(x => x.MarginPercentage).HasPrecision(18,4).IsRequired();
        builder.Property(x => x.SnapshotJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.CreatedBy).IsRequired(false);
        builder.HasOne<RateHeader>().WithMany().HasForeignKey(x => x.RateHeaderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.RateHeaderId, x.RevisionNumber }).IsUnique().HasDatabaseName("ux_rate_revisions_header_number");
        builder.HasIndex(x => x.CreatedAtUtc);
    }
}
''')
replace('src/Dhole.Pricing.Persistence/Configurations/Rates/RateHeaderConfiguration.cs',
'''        builder.Property(x => x.RateName).HasMaxLength(500).IsRequired();\n\n        builder.Property(x => x.ContainerQuantity)''',
'''        builder.Property(x => x.RateName).HasMaxLength(500).IsRequired();\n        builder.Property(x => x.RevisionNumber).IsRequired().HasDefaultValue(1);\n\n        builder.Property(x => x.ContainerQuantity)''')

write('src/Dhole.Pricing.Application/Abstractions/Repositories/IRateRevisionRepository.cs', '''using Dhole.Pricing.Domain.Rates.Entities;

namespace Dhole.Pricing.Application.Abstractions.Repositories;

public interface IRateRevisionRepository
{
    Task AddAsync(RateRevision revision, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<RateRevision>> GetByRateHeaderIdAsync(Guid rateHeaderId, CancellationToken cancellationToken = default);
}
''')
write('src/Dhole.Pricing.Persistence/Repositories/RateRevisionRepository.cs', '''using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Persistence.Repositories;

public sealed class RateRevisionRepository(ServiceDbContext db) : IRateRevisionRepository
{
    public async Task AddAsync(RateRevision revision, CancellationToken cancellationToken = default)
        => await db.Set<RateRevision>().AddAsync(revision, cancellationToken);

    public async Task<IReadOnlyCollection<RateRevision>> GetByRateHeaderIdAsync(Guid rateHeaderId, CancellationToken cancellationToken = default)
        => await db.Set<RateRevision>().AsNoTracking().Where(x => x.RateHeaderId == rateHeaderId)
            .OrderByDescending(x => x.RevisionNumber).ToListAsync(cancellationToken);
}
''')
replace('src/Dhole.Pricing.Persistence/DependencyInjection/PersistenceServiceCollectionExtensions.cs',
'''        services.AddScoped<IRateHeaderRepository, RateHeaderRepository>();''',
'''        services.AddScoped<IRateHeaderRepository, RateHeaderRepository>();\n        services.AddScoped<IRateRevisionRepository, RateRevisionRepository>();''')
replace('src/Dhole.Pricing.Persistence/Repositories/RateHeaderRepository.cs',
'''            .RateHeaders.Include(x => x.RateDetails).Include(x => x.RateContainers)\n''',
'''            .RateHeaders.Include(x => x.RateDetails).Include(x => x.RateContainers).Include(x => x.RateServices)\n''')

# DTOs + history query.
write('src/Dhole.Pricing.Contracts/Rates/Response/RateRevisionDto.cs', '''namespace Dhole.Pricing.Contracts.Rates.Response;

public sealed record RateRevisionDto(
    Guid Id,
    Guid RateHeaderId,
    int RevisionNumber,
    string Status,
    string RateName,
    string? IdtraNumber,
    string? QuoNumber,
    decimal TotalSaleUsd,
    decimal TotalSaleCrc,
    decimal MarginPercentage,
    DateTime CreatedAtUtc,
    Guid? CreatedBy,
    string SnapshotJson
);
''')
replace('src/Dhole.Pricing.Contracts/Rates/Response/RateDto.cs',
'''    string RateName,\n    Guid? SourceImportFclRateId,''',
'''    string RateName,\n    int RevisionNumber,\n    Guid? SourceImportFclRateId,''')
replace('src/Dhole.Pricing.Application/Features/Rates/RateMappings.cs',
'''            rate.RateName,\n            rate.SourceImportFclRateId,''',
'''            rate.RateName,\n            rate.RevisionNumber,\n            rate.SourceImportFclRateId,''')
replace('src/Dhole.Pricing.Persistence/Repositories/RateHeaderRepository.cs',
'''                x.RateName,\n                x.SourceImportFclRateId,''',
'''                x.RateName,\n                x.RevisionNumber,\n                x.SourceImportFclRateId,''')

write('src/Dhole.Pricing.Application/Features/Rates/GetRateRevisions/GetRateRevisionsQuery.cs', '''using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.Pricing.Contracts.Rates.Response;

namespace Dhole.Pricing.Application.Features.Rates.GetRateRevisions;

public sealed record GetRateRevisionsQuery(Guid RateHeaderId) : IQuery<Result<IReadOnlyCollection<RateRevisionDto>>>;
''')
write('src/Dhole.Pricing.Application/Features/Rates/GetRateRevisions/GetRateRevisionsQueryHandler.cs', '''using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Contracts.Rates.Response;
using Dhole.Pricing.Domain.Shared;

namespace Dhole.Pricing.Application.Features.Rates.GetRateRevisions;

public sealed class GetRateRevisionsQueryHandler(IRateHeaderRepository rates, IRateRevisionRepository revisions)
    : IQueryHandler<GetRateRevisionsQuery, Result<IReadOnlyCollection<RateRevisionDto>>>
{
    public async Task<Result<IReadOnlyCollection<RateRevisionDto>>> HandleAsync(GetRateRevisionsQuery query, CancellationToken cancellationToken = default)
    {
        var rate = await rates.GetByIdWithDetailsAsync(query.RateHeaderId, cancellationToken);
        if (rate is null || rate.IsDeleted)
            return Result.Failure<IReadOnlyCollection<RateRevisionDto>>(PricingErrors.RateHeaderNotFound);

        var items = await revisions.GetByRateHeaderIdAsync(query.RateHeaderId, cancellationToken);
        return Result.Success<IReadOnlyCollection<RateRevisionDto>>(items.Select(x => new RateRevisionDto(
            x.Id, x.RateHeaderId, x.RevisionNumber, x.Status, x.RateName, x.IdtraNumber, x.QuoNumber,
            x.TotalSaleUsd, x.TotalSaleCrc, x.MarginPercentage, x.CreatedAtUtc, x.CreatedBy, x.SnapshotJson)).ToList());
    }
}
''')

write('src/Dhole.Pricing.Application/Features/Rates/RateRevisionSnapshotFactory.cs', '''using System.Text.Json;
using Dhole.Pricing.Domain.Rates.Entities;

namespace Dhole.Pricing.Application.Features.Rates;

internal sealed record RateRevisionSnapshotData(
    string Status, string RateName, string? IdtraNumber, string? QuoNumber,
    decimal TotalSaleUsd, decimal TotalSaleCrc, decimal MarginPercentage, string Json);

internal static class RateRevisionSnapshotFactory
{
    public static RateRevisionSnapshotData Capture(RateHeader rate)
    {
        var json = JsonSerializer.Serialize(new
        {
            rate.Id, rate.RateCode, rate.RateName, rate.RevisionNumber, rate.Status,
            rate.ClientName, rate.ExecutiveName, rate.IdtraNumber, rate.QuoNumber,
            rate.AgentId, rate.AgentName, rate.AgentCode, rate.CarrierId, rate.CarrierName, rate.CarrierCode,
            rate.PolId, rate.PolName, rate.PolCode, rate.PoeId, rate.PoeName, rate.PoeCode,
            rate.PodId, rate.PodName, rate.PodCode, rate.ContainerTypeId, rate.ContainerTypeName, rate.ContainerTypeCode,
            rate.IncotermId, rate.IncotermName, rate.IncotermCode, rate.PickupAddress, rate.PickupLatitude, rate.PickupLongitude,
            rate.CurrencyId, rate.CurrencyName, rate.CurrencyCode, rate.ExchangeRatePurchase, rate.ExchangeRateSale,
            rate.ExchangeRateApplied, rate.ExchangeRateDate, rate.ExchangeRateSource, rate.FreeDays, rate.ValidFrom, rate.ValidTo,
            rate.ContainerQuantity, rate.ShipmentMode, rate.OperationType, rate.TotalPackages, rate.TotalPallets,
            rate.TotalWeightKg, rate.TotalVolumeCbm, rate.KgPerCbm, rate.ChargeableQuantity, rate.CargoLinesJson,
            rate.Includes, rate.SubjectTo, rate.Excludes, rate.TransitTime, rate.RateType,
            rate.TotalCostAmount, rate.TotalSaleAmount, rate.TotalUtilityAmount,
            rate.TotalCostUsd, rate.TotalSaleUsd, rate.TotalUtilityUsd, rate.TotalCostCrc, rate.TotalSaleCrc, rate.TotalUtilityCrc,
            rate.MarginPercentage, rate.RequiredApproval,
            Containers = rate.RateContainers.Select(x => new { x.ContainerTypeId, x.ContainerTypeName, x.ContainerTypeCode, x.Quantity }),
            Services = rate.RateServices.Select(x => new { x.ServiceId, x.ServiceName, x.ServiceCode }),
            Details = rate.RateDetails.Select(x => new { x.Id, x.CostId, x.Name, x.CostDetailType, x.CostType, x.ChargeBasis,
                x.CurrencyId, x.CurrencyName, x.CurrencyCode, x.CostAmount, x.SaleAmount, x.UtilityAmount, x.Quantity, x.Notes })
        });
        return new(rate.Status.ToString(), rate.RateName, rate.IdtraNumber, rate.QuoNumber,
            rate.TotalSaleUsd, rate.TotalSaleCrc, rate.MarginPercentage, json);
    }
}
''')

# Update handler: snapshot accepted current revision; create history only after mutation succeeds; increment current revision.
replace('src/Dhole.Pricing.Application/Features/Rates/UpdateRate/UpdateRateCommandHandler.cs',
'''    IRateHeaderRepository rateHeaders,\n    IRateFixedCostSynchronizer fixedCostSynchronizer,''',
'''    IRateHeaderRepository rateHeaders,\n    IRateRevisionRepository rateRevisions,\n    IRateFixedCostSynchronizer fixedCostSynchronizer,''')
replace('src/Dhole.Pricing.Application/Features/Rates/UpdateRate/UpdateRateCommandHandler.cs',
'''        if (rate.SourceImportFclRateId.HasValue && command.ShipmentMode != ShipmentMode.Fcl)''',
'''        var acceptedRevision = rate.Status == Dhole.Pricing.Domain.Rates.Enums.RateStatus.AcceptedByClient\n            ? RateRevisionSnapshotFactory.Capture(rate)\n            : null;\n        var acceptedRevisionNumber = rate.RevisionNumber;\n\n        if (rate.SourceImportFclRateId.HasValue && command.ShipmentMode != ShipmentMode.Fcl)''')
# Locate final audit publish after mutation; there is one header publish near the end and detail publishes after it. Insert immediately before first main audit publish after try.
text=read('src/Dhole.Pricing.Application/Features/Rates/UpdateRate/UpdateRateCommandHandler.cs')
needle='''        await audit.PublishAsync(\n            new PricingAuditEvent(\n                EventType: PricingAuditEventTypes.RateHeaderUpdated,'''
pos=text.find(needle)
if pos < 0: raise RuntimeError('UpdateRate audit anchor not found')
insert='''        if (acceptedRevision is not null)\n        {\n            await rateRevisions.AddAsync(\n                RateRevision.Create(\n                    rate.Id, acceptedRevisionNumber, acceptedRevision.Status, acceptedRevision.RateName,\n                    acceptedRevision.IdtraNumber, acceptedRevision.QuoNumber, acceptedRevision.TotalSaleUsd,\n                    acceptedRevision.TotalSaleCrc, acceptedRevision.MarginPercentage, acceptedRevision.Json, command.UpdatedBy\n                ),\n                cancellationToken\n            );\n            rate.BeginRevision(command.UpdatedBy);\n        }\n\n'''
text=text[:pos]+insert+text[pos:]
write('src/Dhole.Pricing.Application/Features/Rates/UpdateRate/UpdateRateCommandHandler.cs',text)

# API history endpoint.
replace('src/Dhole.Pricing.Api/Endpoints/RateEndpoints.cs',
'''using Dhole.Pricing.Application.Features.Rates.GetRateById;''',
'''using Dhole.Pricing.Application.Features.Rates.GetRateById;\nusing Dhole.Pricing.Application.Features.Rates.GetRateRevisions;''')
replace('src/Dhole.Pricing.Api/Endpoints/RateEndpoints.cs',
'''        group\n            .MapGet("/{rateId:guid}", GetRateByIdAsync)\n            .RequireScope(PricingConstants.Scopes.RateView);''',
'''        group\n            .MapGet("/{rateId:guid}", GetRateByIdAsync)\n            .RequireScope(PricingConstants.Scopes.RateView);\n\n        group\n            .MapGet("/{rateId:guid}/revisions", GetRateRevisionsAsync)\n            .RequireScope(PricingConstants.Scopes.RateView);''')
replace('src/Dhole.Pricing.Api/Endpoints/RateEndpoints.cs',
'''    private static async Task<IResult> CreateRateAsync(''',
'''    private static async Task<IResult> GetRateRevisionsAsync(\n        Guid rateId, IQueryDispatcher dispatcher, HttpContext httpContext, CancellationToken cancellationToken)\n    {\n        var result = await dispatcher.DispatchAsync(new GetRateRevisionsQuery(rateId), cancellationToken);\n        return EndpointResults.FromResult(result, httpContext);\n    }\n\n    private static async Task<IResult> CreateRateAsync(''')

print('Rate revision implementation applied.')

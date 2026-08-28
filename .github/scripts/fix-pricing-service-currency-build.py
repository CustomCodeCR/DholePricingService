from pathlib import Path

p = Path('.github/scripts/apply-pricing-service-currencies-20260828.py')
t = p.read_text(encoding='utf-8')
anchor = '# -----------------------------------------------------------------------------\n# Cost <-> pricing-services association\n'
if anchor not in t:
    raise SystemExit('Cost association anchor not found')
addition = '''# Current UpdateCost handler does not otherwise reference Cost entities.
replace_once(
    'src/Dhole.Pricing.Application/Features/Costs/UpdateCost/UpdateCostCommandHandler.cs',
''' + "'''using Dhole.Pricing.Application.Services;\nusing Dhole.Pricing.Domain.Shared;\n'''" + ''',
''' + "'''using Dhole.Pricing.Application.Services;\nusing Dhole.Pricing.Domain.Costs.Entities;\nusing Dhole.Pricing.Domain.Shared;\n'''" + ''',
)

# Rate endpoints build service selections from request DTOs.
replace_once(
    'src/Dhole.Pricing.Api/Endpoints/RateEndpoints.cs',
''' + "'''using Dhole.Pricing.Domain.Costs.Enums;\nusing Dhole.Pricing.Domain.Rates.Enums;\n'''" + ''',
''' + "'''using Dhole.Pricing.Domain.Costs.Enums;\nusing Dhole.Pricing.Domain.Rates.Entities;\nusing Dhole.Pricing.Domain.Rates.Enums;\n'''" + ''',
)

# Paged RateDto is projected directly in persistence, so keep it aligned with the enriched contract.
rate_repository = 'src/Dhole.Pricing.Persistence/Repositories/RateHeaderRepository.cs'
replace_all(
    rate_repository,
''' + "'''                .Include(x => x.RateContainers)\n'''" + ''',
''' + "'''                .Include(x => x.RateContainers)\n                .Include(x => x.RateServices)\n'''" + ''',
)
replace_once(
    rate_repository,
''' + "'''                x.RateType.ToString(),\n                x.ShipmentMode.ToString(),\n                x.TotalPackages,\n'''" + ''',
''' + "'''                x.RateType.ToString(),\n                x.ShipmentMode.ToString(),\n                x.OperationType.ToString(),\n                x.TotalPackages,\n'''" + ''',
)
replace_once(
    rate_repository,
''' + "'''                x.TotalCostAmount,\n                x.TotalSaleAmount,\n                x.TotalUtilityAmount,\n                x.MarginPercentage,\n'''" + ''',
''' + "'''                x.TotalCostAmount,\n                x.TotalSaleAmount,\n                x.TotalUtilityAmount,\n                x.TotalCostUsd,\n                x.TotalSaleUsd,\n                x.TotalUtilityUsd,\n                x.TotalCostCrc,\n                x.TotalSaleCrc,\n                x.TotalUtilityCrc,\n                x.MarginPercentage,\n'''" + ''',
)
replace_once(
    rate_repository,
''' + "'''                    ))\n                    .ToList()\n            ))\n'''" + ''',
''' + "'''                    ))\n                    .ToList(),\n                x.RateServices\n                    .OrderBy(s => s.ServiceName)\n                    .Select(s => new RateServiceDto(s.ServiceId, s.ServiceName, s.ServiceCode))\n                    .ToList()\n            ))\n'''" + ''',
)

# Cache warmup worker projects the same CostDto/RateDto contracts and must load the new relations.
worker = 'src/Dhole.Pricing.Workers/Workers/PricingCacheWarmupWorker.cs'
replace_once(
    worker,
''' + "'''            .Costs.AsNoTracking()\n            .Include(x => x.Incoterms)\n'''" + ''',
''' + "'''            .Costs.AsNoTracking()\n            .Include(x => x.Incoterms)\n            .Include(x => x.Services)\n'''" + ''',
)
replace_once(
    worker,
''' + "'''            .RateHeaders.AsNoTracking()\n            .Include(x => x.RateDetails)\n            .Include(x => x.RateContainers)\n'''" + ''',
''' + "'''            .RateHeaders.AsNoTracking()\n            .Include(x => x.RateDetails)\n            .Include(x => x.RateContainers)\n            .Include(x => x.RateServices)\n'''" + ''',
)
replace_once(
    worker,
''' + "'''            rate.RateType.ToString(),\n            rate.ShipmentMode.ToString(),\n            rate.TotalPackages,\n'''" + ''',
''' + "'''            rate.RateType.ToString(),\n            rate.ShipmentMode.ToString(),\n            rate.OperationType.ToString(),\n            rate.TotalPackages,\n'''" + ''',
)
replace_once(
    worker,
''' + "'''            rate.TotalCostAmount,\n            rate.TotalSaleAmount,\n            rate.TotalUtilityAmount,\n            rate.MarginPercentage,\n'''" + ''',
''' + "'''            rate.TotalCostAmount,\n            rate.TotalSaleAmount,\n            rate.TotalUtilityAmount,\n            rate.TotalCostUsd,\n            rate.TotalSaleUsd,\n            rate.TotalUtilityUsd,\n            rate.TotalCostCrc,\n            rate.TotalSaleCrc,\n            rate.TotalUtilityCrc,\n            rate.MarginPercentage,\n'''" + ''',
)
replace_once(
    worker,
''' + "'''            rate.RateDetails.OrderBy(x => x.CostType)\n                .ThenBy(x => x.CostDetailType)\n                .ThenBy(x => x.Name)\n                .Select(ToRateDetailDto)\n                .ToList()\n        );\n'''" + ''',
''' + "'''            rate.RateDetails.OrderBy(x => x.CostType)\n                .ThenBy(x => x.CostDetailType)\n                .ThenBy(x => x.Name)\n                .Select(ToRateDetailDto)\n                .ToList(),\n            rate.RateServices\n                .OrderBy(x => x.ServiceName)\n                .Select(x => new RateServiceDto(x.ServiceId, x.ServiceName, x.ServiceCode))\n                .ToList()\n        );\n'''" + ''',
)

'''
t = t.replace(anchor, addition + anchor, 1)
p.write_text(t, encoding='utf-8')
print('pricing currency build compatibility patch applied')

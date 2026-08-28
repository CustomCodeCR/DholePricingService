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

'''
t = t.replace(anchor, addition + anchor, 1)
p.write_text(t, encoding='utf-8')
print('pricing currency build compatibility patch applied')

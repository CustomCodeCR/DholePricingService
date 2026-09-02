from pathlib import Path
import re

endpoint_path = Path('src/Dhole.Pricing.Api/Endpoints/OwnLclDestinationAutomationEndpoints.cs')
text = endpoint_path.read_text(encoding='utf-8')

# Dependencies for reading the real Pricing cost matrix.
needle = 'using Dhole.Pricing.Application.Abstractions.Services;\nusing Dhole.Pricing.Domain.Shared;'
replacement = 'using Dhole.Pricing.Application.Abstractions.Services;\nusing Dhole.Pricing.Domain.Costs.Enums;\nusing Dhole.Pricing.Domain.Rates.Enums;\nusing Dhole.Pricing.Domain.Shared;'
if needle not in text:
    raise SystemExit('Could not find endpoint using anchor')
text = text.replace(needle, replacement, 1)

# Preview receives the selected equipment code as well, so PerTEU costs can be projected correctly.
old_preview = '''        decimal? maximumCbm,\n        bool? includeEmptyReturn,\n        IPricingConfigCatalogClient config,\n        CancellationToken ct)'''
new_preview = '''        decimal? maximumCbm,\n        bool? includeEmptyReturn,\n        string? containerCode,\n        IPricingConfigCatalogClient config,\n        ServiceDbContext db,\n        CancellationToken ct)'''
if old_preview not in text:
    raise SystemExit('Could not find PreviewAsync signature anchor')
text = text.replace(old_preview, new_preview, 1)

old_preview_call = '''            includeEmptyReturn,\n            config,\n            ct);'''
new_preview_call = '''            includeEmptyReturn,\n            containerCode,\n            config,\n            db,\n            ct);'''
if old_preview_call not in text:
    raise SystemExit('Could not find Preview ResolveAsync call')
text = text.replace(old_preview_call, new_preview_call, 1)

# Create and update use the same resolver and therefore the same source of truth as preview.
old_request_call = '''            request.IncludeEmptyReturn,\n            config,\n            ct);'''
new_request_call = '''            request.IncludeEmptyReturn,\n            request.ContainerCode,\n            config,\n            db,\n            ct);'''
if text.count(old_request_call) != 2:
    raise SystemExit(f'Expected 2 create/update resolver calls, found {text.count(old_request_call)}')
text = text.replace(old_request_call, new_request_call)

old_resolve_signature = '''        decimal maximumCbm,\n        bool? includeEmptyReturn,\n        IPricingConfigCatalogClient config,\n        CancellationToken ct)'''
new_resolve_signature = '''        decimal maximumCbm,\n        bool? includeEmptyReturn,\n        string? containerCode,\n        IPricingConfigCatalogClient config,\n        ServiceDbContext db,\n        CancellationToken ct)'''
if old_resolve_signature not in text:
    raise SystemExit('Could not find ResolveAsync signature')
text = text.replace(old_resolve_signature, new_resolve_signature, 1)

matrix_hook_anchor = '''        var portCandidate = NormalizeMatch(arrivalPortCode);\n        if (carrierCandidates.Count == 0 || portCandidate.Length == 0) return null;\n\n        var items = await config.GetActiveByGroupAsync(ProfileCatalogSlug, ct);'''
matrix_hook = '''        var portCandidate = NormalizeMatch(arrivalPortCode);\n        if (carrierCandidates.Count == 0 || portCandidate.Length == 0) return null;\n\n        // Pricing's Cost Matrix is the source of truth for own-LCL destination costs.\n        // Config profiles remain only as a backwards-compatible fallback.\n        var matrixProfile = await ResolveFromCostMatrixAsync(\n            carrierCandidates,\n            portCandidate,\n            arrivalPortCode,\n            maximumCbm,\n            includeEmptyReturn,\n            containerCode,\n            db,\n            ct);\n        if (matrixProfile is not null) return matrixProfile;\n\n        var items = await config.GetActiveByGroupAsync(ProfileCatalogSlug, ct);'''
if matrix_hook_anchor not in text:
    raise SystemExit('Could not find matrix hook anchor')
text = text.replace(matrix_hook_anchor, matrix_hook, 1)

helper_anchor = '    private static DestinationProfileDefinition? ParseDefinition(PricingConfigCatalogItem item)\n'
if helper_anchor not in text:
    raise SystemExit('Could not find ParseDefinition anchor')

helper = r'''    private static async Task<AutomaticDestinationProfileDto?> ResolveFromCostMatrixAsync(
        IReadOnlySet<string> carrierCandidates,
        string portCandidate,
        string? arrivalPortCode,
        decimal maximumCbm,
        bool? includeEmptyReturn,
        string? containerCode,
        ServiceDbContext db,
        CancellationToken ct)
    {
        var candidates = await db.Costs
            .AsNoTracking()
            .Where(cost =>
                cost.IsActive
                // Destination matrix entries are shared by the maritime FCL/LCL flows when
                // carrier + POE match. Older PerContainer entries can be marked FCL by the
                // legacy factory, so filtering them out would incorrectly hide valid LCL costs.
                && cost.ShipmentMode != ShipmentMode.Ftl
                && cost.ShipmentMode != ShipmentMode.Ltl
                && cost.PolId == null
                && cost.PodId == null
                && cost.CurrencyCode == "USD"
                && cost.CostDetailType != CostDetailType.Freight
                && cost.CostDetailType != CostDetailType.OriginCharge
                && cost.CostDetailType != CostDetailType.Insurance)
            .ToListAsync(ct);

        bool CarrierMatches(Dhole.Pricing.Domain.Costs.Entities.Cost cost)
        {
            var hasCarrierRestriction = cost.CarrierId.HasValue
                || !string.IsNullOrWhiteSpace(cost.CarrierCode)
                || !string.IsNullOrWhiteSpace(cost.CarrierName);
            if (!hasCarrierRestriction) return true;

            return new[] { cost.CarrierCode, cost.CarrierName }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(NormalizeMatch)
                .Any(carrierCandidates.Contains);
        }

        bool PortMatches(Dhole.Pricing.Domain.Costs.Entities.Cost cost)
        {
            var structured = new[] { cost.PoeCode, cost.PoeName }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(NormalizeMatch)
                .Any(value => value == portCandidate);
            if (structured) return true;

            if (cost.PortRole != CostPortRole.Poe) return false;
            return new[] { cost.PortCode, cost.PortName }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(NormalizeMatch)
                .Any(value => value == portCandidate);
        }

        var matches = candidates
            .Where(cost => CarrierMatches(cost) && PortMatches(cost))
            .OrderBy(cost => cost.Name)
            .ToArray();
        if (matches.Length == 0) return null;

        var useEmptyReturn = includeEmptyReturn ?? true;
        var compactContainer = NormalizeMatch(containerCode);
        var teuMultiplier = compactContainer.StartsWith("20", StringComparison.Ordinal) ? 1m
            : compactContainer.StartsWith("40", StringComparison.Ordinal)
                || compactContainer.StartsWith("45", StringComparison.Ordinal)
                || compactContainer.StartsWith("48", StringComparison.Ordinal)
                || compactContainer.StartsWith("53", StringComparison.Ordinal)
                ? 2m
                : 1m;

        decimal ProjectAmount(Dhole.Pricing.Domain.Costs.Entities.Cost cost)
        {
            var projected = cost.ChargeBasis switch
            {
                ChargeBasis.PerCbm or ChargeBasis.PerChargeableCbm => cost.CostAmount * maximumCbm,
                ChargeBasis.PerTeu => cost.CostAmount * teuMultiplier,
                _ => cost.CostAmount,
            };
            return Math.Max(projected, cost.MinimumCostAmount ?? 0m);
        }

        bool IsEmptyReturn(Dhole.Pricing.Domain.Costs.Entities.Cost cost)
        {
            var name = NormalizeMatch(cost.Name);
            return name.Contains("EMPTYRETURN", StringComparison.Ordinal)
                || name.Contains("RETIRODEVACIO", StringComparison.Ordinal)
                || name.Contains("RETIROVACIO", StringComparison.Ordinal)
                || name.Contains("VACIOYROLEO", StringComparison.Ordinal);
        }

        var charges = matches
            .Select(cost =>
            {
                var emptyReturn = IsEmptyReturn(cost);
                var optional = cost.CostType == CostType.Optional || emptyReturn;
                var included = !emptyReturn || useEmptyReturn;
                return new AutomaticDestinationChargeDto(
                    $"COST-{cost.Id:N}",
                    cost.Name,
                    ProjectAmount(cost),
                    cost.ChargeBasis.ToString(),
                    !optional,
                    optional,
                    included,
                    new[]
                    {
                        cost.CostDetailType.ToString(),
                        cost.CostType.ToString(),
                        cost.ChargeBasis.ToString(),
                    });
            })
            .ToArray();

        var total = charges.Where(charge => charge.Included).Sum(charge => charge.Amount);
        var carrierLabel = matches.Select(cost => cost.CarrierName)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? matches.Select(cost => cost.CarrierCode).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? "Naviera";
        var portLabel = matches.Select(cost => cost.PoeName)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? matches.Select(cost => cost.PortName).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? Normalize(arrivalPortCode);

        var profileCode = $"MATRIX-{NormalizeMatch(carrierLabel)}-{portCandidate}";
        if (profileCode.Length > 90) profileCode = profileCode[..90];

        return new AutomaticDestinationProfileDto(
            profileCode,
            "MATRIX-LIVE",
            $"Matriz de costos · {carrierLabel} · {portLabel}",
            "USD",
            Normalize(arrivalPortCode),
            "CFZ",
            "Colón Free Zone",
            useEmptyReturn,
            charges,
            total,
            total / Math.Max(0.01m, maximumCbm),
            // Existing China -> Central America operational baseline. It remains a separate
            // transfer component while destination charges come from the live Cost Matrix.
            new CostaRicaTransferDto(2140m, 280m, 95m),
            false,
            "Pricing: Matriz de costos (naviera + POE)");
    }

'''
text = text.replace(helper_anchor, helper + helper_anchor, 1)

# Messages must describe the actual sources rather than instructing users to create a duplicate profile.
text = text.replace(
    'No hay cargos en destino configurados para la combinación de naviera y puerto de llegada en Panamá.',
    'No hay costos activos aplicables en la Matriz de costos para la combinación de naviera y POE seleccionada.'
)
text = text.replace(
    'Configure en Config los cargos de esta naviera para el puerto de llegada seleccionado antes de crear el consolidado.',
    'Configure los cargos de esta naviera + POE en la Matriz de costos de Pricing antes de crear el consolidado.'
)
text = text.replace(
    'No existe un perfil automático para esta naviera y puerto de llegada. El costo no puede ingresarse manualmente desde Pricing.',
    'No existen costos automáticos para esta naviera + POE en la Matriz de costos. El costo no puede ingresarse manualmente.'
)

endpoint_path.write_text(text, encoding='utf-8')

# QR is patched in a separate workflow step to the stable isolated /origin page.
print('Own LCL destination costs now resolve from Pricing Cost Matrix.')

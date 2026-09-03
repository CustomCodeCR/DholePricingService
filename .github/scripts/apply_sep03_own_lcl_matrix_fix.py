from pathlib import Path

path = Path('src/Dhole.Pricing.Application/Services/RateFixedCostSynchronizer.cs')
text = path.read_text(encoding='utf-8')

entry_old = '''    {\n        var existingAmounts = rate\n'''
entry_new = '''    {\n        // LCL propio ya lleva la matriz del consolidado prorrateada dentro del flete/CBM.\n        // En la tarifa comercial solo deben persistir el flete y las líneas adicionales\n        // provenientes de las reglas/Excel, que no están ligadas a CostId.\n        if (IsOwnLclRate(rate))\n        {\n            var configuredDetailIds = rate.RateDetails\n                .Where(x => x.CostId.HasValue)\n                .Select(x => x.Id)\n                .ToArray();\n\n            foreach (var detailId in configuredDetailIds)\n            {\n                rate.RemoveRateDetail(detailId, updatedBy);\n            }\n\n            return;\n        }\n\n        var existingAmounts = rate\n'''
if entry_new not in text:
    if entry_old not in text:
        raise RuntimeError('Could not find SynchronizeAsync insertion point')
    text = text.replace(entry_old, entry_new, 1)

method_old = '''    private static bool MatchesRate(Cost cost, RateHeader rate)\n    {\n'''
method_new = '''    private static bool IsOwnLclRate(RateHeader rate)\n    {\n        if (rate.ShipmentMode != ShipmentMode.Lcl)\n            return false;\n\n        var headerMarker = NormalizeRouteText(rate.RateName);\n        if (headerMarker.Contains("LCL PROPIO", StringComparison.Ordinal)\n            || headerMarker.Contains("CONSOLIDADO PROPIO", StringComparison.Ordinal))\n            return true;\n\n        return rate.RateDetails.Any(detail =>\n        {\n            if (detail.CostDetailType != CostDetailType.Freight || detail.CostId.HasValue)\n                return false;\n\n            var marker = NormalizeRouteText($"{detail.Name} {detail.Notes}");\n            return marker.Contains("LCL PROPIO", StringComparison.Ordinal)\n                || marker.Contains("CONSOLIDADO PROPIO", StringComparison.Ordinal);\n        });\n    }\n\n    private static bool MatchesRate(Cost cost, RateHeader rate)\n    {\n'''
if method_new not in text:
    if method_old not in text:
        raise RuntimeError('Could not find MatchesRate insertion point')
    text = text.replace(method_old, method_new, 1)

path.write_text(text, encoding='utf-8')
print('Own LCL matrix duplication protection applied.')

from pathlib import Path

path = Path('src/Dhole.Pricing.Application/Features/Rates/DuplicateRate/DuplicateRateCommandHandler.cs')
text = path.read_text(encoding='utf-8')

old = '''            pod = await configCatalog.GetActiveInGroupAsync(
                source.PodId, PricingConstants.CatalogSlugs.Pod, cancellationToken);
'''
new = '''            pod = source.PodId.HasValue
                ? await configCatalog.GetActiveInGroupAsync(
                    source.PodId, PricingConstants.CatalogSlugs.Pod, cancellationToken)
                : null;
'''
if old not in text:
    raise SystemExit('POD lookup anchor not found')
text = text.replace(old, new, 1)

old = '''            if (pod is null) return Result.Failure<Guid>(PricingErrors.InvalidConfigCatalogReference(
                "El POD", PricingConstants.CatalogSlugs.Pod));
'''
new = '''            if (source.PodId.HasValue && pod is null)
                return Result.Failure<Guid>(PricingErrors.InvalidConfigCatalogReference(
                    "El POD", PricingConstants.CatalogSlugs.Pod));
'''
if old not in text:
    raise SystemExit('POD validation anchor not found')
text = text.replace(old, new, 1)

old = '''                rateCode,
                sourceImportFclRateId: null,
'''
new = '''                rateCode,
                sourceImportFclRateId: source.SourceImportFclRateId,
'''
if old not in text:
    raise SystemExit('source import lineage anchor not found')
text = text.replace(old, new, 1)

old = '''                pod.Id,
                pod.SnapshotName(),
                pod.Code,
'''
new = '''                pod?.Id,
                pod?.SnapshotName(),
                pod?.Code,
'''
if old not in text:
    raise SystemExit('POD snapshot anchor not found')
text = text.replace(old, new, 1)

old = '''                source.ClientName,
                null,
                null,
                source.Includes,
'''
new = '''                source.ClientName,
                null,
                rateCode,
                source.Includes,
'''
if old not in text:
    raise SystemExit('duplicate QUO anchor not found')
text = text.replace(old, new, 1)

path.write_text(text, encoding='utf-8')
print('Duplicate current-flow patch applied')

from pathlib import Path


def patch(path_text: str, old: str, new: str, label: str) -> None:
    path = Path(path_text)
    text = path.read_text(encoding='utf-8')
    if old not in text:
        raise SystemExit(f'{label} marker not found')
    path.write_text(text.replace(old, new, 1), encoding='utf-8')
    print(f'{label} patched.')


patch(
    'src/Dhole.Pricing.Persistence/Repositories/RateHeaderRepository.cs',
    '                x.ClientName,\n                x.IdtraNumber,',
    '                x.ClientName,\n                x.ExecutiveName,\n                x.IdtraNumber,',
    'RateHeaderRepository executive projection',
)

patch(
    'src/Dhole.Pricing.Workers/Workers/PricingCacheWarmupWorker.cs',
    '            rate.ClientName,\n            rate.IdtraNumber,',
    '            rate.ClientName,\n            rate.ExecutiveName,\n            rate.IdtraNumber,',
    'PricingCacheWarmupWorker executive projection',
)

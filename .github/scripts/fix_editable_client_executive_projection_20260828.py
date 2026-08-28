from pathlib import Path

path = Path('src/Dhole.Pricing.Persistence/Repositories/RateHeaderRepository.cs')
text = path.read_text(encoding='utf-8')
old = '                x.ClientName,\n                x.IdtraNumber,'
new = '                x.ClientName,\n                x.ExecutiveName,\n                x.IdtraNumber,'
if old not in text:
    raise SystemExit('RateHeaderRepository RateDto projection marker not found')
path.write_text(text.replace(old, new, 1), encoding='utf-8')
print('RateHeaderRepository executive projection patched.')

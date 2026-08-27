from pathlib import Path

path = Path('src/Dhole.Pricing.Domain/Rates/Entities/RateHeader.cs')
text = path.read_text(encoding='utf-8-sig')
old = '''        if (Containers.Count > 0)
        {
            return Containers.Sum(container =>'''
new = '''        if (_rateContainers.Count > 0)
        {
            return _rateContainers.Sum(container =>'''
count = text.count(old)
if count != 1:
    raise SystemExit(f'expected TEU container block once, found {count}')
path.write_text(text.replace(old, new, 1), encoding='utf-8')

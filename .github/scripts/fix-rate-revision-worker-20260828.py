from pathlib import Path

path = Path('src/Dhole.Pricing.Workers/Workers/PricingCacheWarmupWorker.cs')
text = path.read_text(encoding='utf-8')
old = '''            rate.RateCode,\n            rate.RateName,\n            rate.SourceImportFclRateId,'''
new = '''            rate.RateCode,\n            rate.RateName,\n            rate.RevisionNumber,\n            rate.SourceImportFclRateId,'''
count = text.count(old)
if count != 1:
    raise RuntimeError(f'Expected one worker RateDto projection, found {count}')
path.write_text(text.replace(old, new, 1), encoding='utf-8')
print('Pricing cache worker RateDto projection updated with RevisionNumber.')

from pathlib import Path

p = Path('src/Dhole.Pricing.Workers/Workers/PricingCacheWarmupWorker.cs')
text = p.read_text()
old = '''            rate.CurrencyId,
            rate.CurrencyName,
            rate.CurrencyCode,
            rate.FreeDays,
'''
new = '''            rate.CurrencyId,
            rate.CurrencyName,
            rate.CurrencyCode,
            rate.ExchangeRatePurchase,
            rate.ExchangeRateSale,
            rate.ExchangeRateApplied,
            rate.ExchangeRateDate,
            rate.ExchangeRateCapturedAtUtc,
            rate.ExchangeRateSource,
            rate.ExchangeRateManualOverride,
            rate.FreeDays,
'''
if old not in text:
    raise SystemExit('Worker RateDto projection marker not found')
p.write_text(text.replace(old, new, 1))
print('Worker RateDto projection patched')

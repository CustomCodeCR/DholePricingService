from pathlib import Path
p = Path('src/Dhole.Pricing.Persistence/Repositories/RateHeaderRepository.cs')
text = p.read_text()
old = '''                x.CurrencyId,
                x.CurrencyName,
                x.CurrencyCode,
                x.FreeDays,
'''
new = '''                x.CurrencyId,
                x.CurrencyName,
                x.CurrencyCode,
                x.ExchangeRatePurchase,
                x.ExchangeRateSale,
                x.ExchangeRateApplied,
                x.ExchangeRateDate,
                x.ExchangeRateCapturedAtUtc,
                x.ExchangeRateSource,
                x.ExchangeRateManualOverride,
                x.FreeDays,
'''
if old not in text:
    raise SystemExit('RateDto browse projection marker not found')
p.write_text(text.replace(old, new, 1))
print('RateDto browse projection patched')

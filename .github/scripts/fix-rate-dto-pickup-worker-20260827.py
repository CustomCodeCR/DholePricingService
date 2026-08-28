from pathlib import Path

path = Path('src/Dhole.Pricing.Workers/Workers/PricingCacheWarmupWorker.cs')
text = path.read_text(encoding='utf-8')
old = '''            rate.IncotermId,
            rate.IncotermName,
            rate.IncotermCode,
            rate.ContainerQuantity,'''
new = '''            rate.IncotermId,
            rate.IncotermName,
            rate.IncotermCode,
            rate.PickupAddress,
            rate.PickupLatitude,
            rate.PickupLongitude,
            rate.ContainerQuantity,'''
if old not in text:
    raise SystemExit('No se encontró la proyección RateDto del worker.')
text = text.replace(old, new, 1)
path.write_text(text, encoding='utf-8')
print('Worker RateDto pickup projection fixed.')

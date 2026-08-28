from pathlib import Path

path = Path('src/Dhole.Pricing.Persistence/Repositories/RateHeaderRepository.cs')
text = path.read_text(encoding='utf-8')
old = '''                x.IncotermId,
                x.IncotermName,
                x.IncotermCode,
                x.ContainerQuantity,'''
new = '''                x.IncotermId,
                x.IncotermName,
                x.IncotermCode,
                x.PickupAddress,
                x.PickupLatitude,
                x.PickupLongitude,
                x.ContainerQuantity,'''
if old not in text:
    raise SystemExit('No se encontró la proyección RateDto esperada.')
text = text.replace(old, new, 1)
path.write_text(text, encoding='utf-8')
print('RateDto pickup projection fixed.')

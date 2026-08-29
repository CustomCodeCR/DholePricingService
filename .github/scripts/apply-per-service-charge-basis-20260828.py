from pathlib import Path


def replace_once(path: str, old: str, new: str) -> None:
    file = Path(path)
    text = file.read_text(encoding='utf-8')
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f'{path}: expected one match, found {count}: {old[:120]!r}')
    file.write_text(text.replace(old, new, 1), encoding='utf-8')


replace_once(
    'src/Dhole.Pricing.Domain/Costs/Enums/ChargeBasis.cs',
    '''    PerDocument = 40,\n}''',
    '''    PerDocument = 40,\n    PerService = 50,\n}''',
)

replace_once(
    'src/Dhole.Pricing.Domain/Costs/Entities/Cost.cs',
    '''        if (normalized.Length != selections.Count)\n            throw new InvalidOperationException("Los servicios de Pricing del costo no pueden estar vacíos ni repetidos.");\n\n        var selectedIds = normalized.Select(x => x.Id).ToHashSet();''',
    '''        if (normalized.Length != selections.Count)\n            throw new InvalidOperationException("Los servicios de Pricing del costo no pueden estar vacíos ni repetidos.");\n\n        if (ChargeBasis == Dhole.Pricing.Domain.Costs.Enums.ChargeBasis.PerService && normalized.Length != 1)\n            throw new InvalidOperationException("La base de cobro Por Servicio requiere exactamente un servicio de Pricing.");\n\n        var selectedIds = normalized.Select(x => x.Id).ToHashSet();''',
)

print('Per-service charge basis applied to Pricing.')

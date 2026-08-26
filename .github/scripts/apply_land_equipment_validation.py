from pathlib import Path

FILES = [
    Path('src/Dhole.Pricing.Application/Features/Rates/CreateRate/CreateRateCommandHandler.cs'),
    Path('src/Dhole.Pricing.Application/Features/Rates/UpdateRate/UpdateRateCommandHandler.cs'),
]

for path in FILES:
    text = path.read_text()
    marker = '            foreach (var requested in requestedContainers)\n'
    if text.count(marker) != 1:
        raise SystemExit(f'{path}: requestedContainers foreach marker count={text.count(marker)}')

    insert = '''            var equipmentCatalogSlug = command.ShipmentMode is ShipmentMode.Ftl or ShipmentMode.Ltl
                ? PricingConstants.CatalogSlugs.LandEquipmentTypes
                : PricingConstants.CatalogSlugs.ContainerTypes;
            var equipmentCatalogLabel = command.ShipmentMode is ShipmentMode.Ftl or ShipmentMode.Ltl
                ? "El tipo de unidad terrestre"
                : "El tipo de contenedor";

'''
    text = text.replace(marker, insert + marker, 1)

    old_lookup = '''                    requested.ContainerTypeId,
                    PricingConstants.CatalogSlugs.ContainerTypes,
                    cancellationToken
'''
    if text.count(old_lookup) != 1:
        raise SystemExit(f'{path}: container lookup count={text.count(old_lookup)}')
    text = text.replace(
        old_lookup,
        '''                    requested.ContainerTypeId,
                    equipmentCatalogSlug,
                    cancellationToken
''',
        1,
    )

    generic = '<Guid>' if 'CreateRate' in str(path) else ''
    old_error = f'''                    return Result.Failure{generic}(PricingErrors.InvalidConfigCatalogReference(
                        "El tipo de contenedor", PricingConstants.CatalogSlugs.ContainerTypes));
'''
    if text.count(old_error) != 1:
        raise SystemExit(f'{path}: equipment error count={text.count(old_error)}')
    new_error = f'''                    return Result.Failure{generic}(PricingErrors.InvalidConfigCatalogReference(
                        equipmentCatalogLabel, equipmentCatalogSlug));
'''
    text = text.replace(old_error, new_error, 1)
    path.write_text(text)

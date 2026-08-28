from pathlib import Path

path = Path('src/Dhole.Pricing.Application/Features/Rates/UpdateRate/UpdateRateCommandHandler.cs')
text = path.read_text(encoding='utf-8')

old = '''        var extraDetails = command.ExtraDetails ?? Array.Empty<UpsertRateExtraDetailCommandItem>();

        var removedIds = (command.RemovedExtraDetailIds ?? Array.Empty<Guid>())
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToHashSet();

        var updatedIds = extraDetails.Where(x => x.Id.HasValue).Select(x => x.Id!.Value).ToArray();

        if (updatedIds.Distinct().Count() != updatedIds.Length)
        {
            return Result.Failure(PricingErrors.RateInvalidStatus);
        }

        if (updatedIds.Any(removedIds.Contains))
        {
            return Result.Failure(PricingErrors.RateInvalidStatus);
        }
'''

new = '''        var requestedExtraDetails = command.ExtraDetails ?? Array.Empty<UpsertRateExtraDetailCommandItem>();

        // El wizard puede reconstruir la misma línea desde más de una sección visual
        // (por ejemplo, flete por contenedor + líneas existentes). Eso no es un error de
        // estado de la tarifa: normalizamos el payload y conservamos una sola actualización
        // por detalle persistido. Las líneas nuevas (Id null) se mantienen todas.
        var extraDetails = new List<UpsertRateExtraDetailCommandItem>(requestedExtraDetails.Length);
        var updatedIdSet = new HashSet<Guid>();
        foreach (var detail in requestedExtraDetails)
        {
            if (!detail.Id.HasValue || detail.Id.Value == Guid.Empty)
            {
                extraDetails.Add(detail with { Id = null });
                continue;
            }

            if (updatedIdSet.Add(detail.Id.Value))
            {
                extraDetails.Add(detail);
            }
        }

        // Si una línea está presente en extraDetails significa que sigue viva en el wizard.
        // En ese caso una marca stale de removedExtraDetailIds no debe ganar ni romper el guardado.
        var removedIds = (command.RemovedExtraDetailIds ?? Array.Empty<Guid>())
            .Where(x => x != Guid.Empty && !updatedIdSet.Contains(x))
            .Distinct()
            .ToHashSet();

        var updatedIds = updatedIdSet.ToArray();
'''

if old not in text:
    raise SystemExit('Update payload normalization anchor not found')
text = text.replace(old, new, 1)
path.write_text(text, encoding='utf-8')
print('Resilient rate edit payload patch applied')

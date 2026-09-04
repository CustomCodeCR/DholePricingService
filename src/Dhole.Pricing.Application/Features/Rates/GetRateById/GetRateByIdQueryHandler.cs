using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.Pricing.Application.Abstractions.Cache;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Contracts.Rates.Response;
using Dhole.Pricing.Domain.Shared;

namespace Dhole.Pricing.Application.Features.Rates.GetRateById;

public sealed class GetRateByIdQueryHandler(
    IRateHeaderRepository rateHeaders,
    IRateHeaderCacheService cache
) : IQueryHandler<GetRateByIdQuery, Result<RateDto>>
{
    public async Task<Result<RateDto>> HandleAsync(
        GetRateByIdQuery query,
        CancellationToken cancellationToken = default
    )
    {
        // La vista/edición de una tarifa necesita siempre el snapshot completo y actual de
        // RateDetails. El PDF también se construye desde la entidad persistida, por lo que
        // servir aquí un DTO cacheado puede dejar la UI mostrando solo una versión anterior
        // (por ejemplo únicamente Freight) aunque Manejos/HBL y demás líneas sí existan en DB.
        //
        // Este endpoint es de detalle, no de listado: priorizamos consistencia sobre el hit
        // de caché y refrescamos la caché con el snapshot completo después de leer PostgreSQL.
        var rate = await rateHeaders.GetByIdWithDetailsAsync(query.Id, cancellationToken);

        if (rate is null || rate.IsDeleted)
        {
            return Result.Failure<RateDto>(PricingErrors.RateHeaderNotFound);
        }

        var dto = rate.ToDto();

        await cache.SetRateHeaderByIdAsync(rate.Id, dto, cancellationToken: cancellationToken);

        return Result.Success(dto);
    }
}
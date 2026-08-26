using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Persistence.Services;

public sealed class RateCodeGenerator(ServiceDbContext dbContext) : IRateCodeGenerator
{
    private const long MaximumConsecutive = 99_999_999_999L;

    public async Task<string> GenerateAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            var consecutive = await dbContext.Database
                .SqlQueryRaw<long>("SELECT nextval('pricing.rate_quote_consecutive') AS \"Value\"")
                .SingleAsync(cancellationToken);

            if (consecutive <= 0 || consecutive > MaximumConsecutive)
            {
                throw new InvalidOperationException(
                    "El consecutivo de tarifas de Pricing excedió el rango soportado."
                );
            }

            var digits = consecutive.ToString("D11");
            var candidate = $"QUO-{digits[..5]}-{digits[5..]}";

            // Las tarifas históricas usaron códigos aleatorios. Si alguno coincide por casualidad
            // con el nuevo formato numérico, avanzamos la secuencia sin reutilizar el código.
            var alreadyExists = await dbContext
                .Set<RateHeader>()
                .AsNoTracking()
                .AnyAsync(rate => rate.RateCode == candidate, cancellationToken);

            if (!alreadyExists)
            {
                return candidate;
            }
        }
    }
}
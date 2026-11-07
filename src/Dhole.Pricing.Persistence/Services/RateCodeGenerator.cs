using System.Globalization;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Dhole.Pricing.Persistence.Services;

public sealed class RateCodeGenerator(ServiceDbContext dbContext) : IRateCodeGenerator
{
    private const long MaximumConsecutive = 99_999_999_999L;

    public async Task<IRateCodeReservation> ReserveAsync(CancellationToken cancellationToken = default)
    {
        // No usar nextval(): las secuencias PostgreSQL no revierten incrementos
        // cuando la creación de la cotización falla o se cancela.
        // Grouped QUOs share the outer database transaction. Child reservations
        // must not commit it before the last alternative passes validation.
        var existingTransaction = dbContext.Database.CurrentTransaction;
        var transaction = existingTransaction ?? await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var ownsTransaction = existingTransaction is null;

        try
        {
            while (true)
            {
                // Un UPDATE de una única fila serializa las reservas entre todas las
                // instancias y participa de la misma transacción que RateHeader.
                // Si no hay SaveChanges + Commit, el incremento se revierte.
                long consecutive;
                await using (var command = dbContext.Database.GetDbConnection().CreateCommand())
                {
                    command.Transaction = transaction.GetDbTransaction();
                    command.CommandText = """
                        UPDATE pricing.rate_quote_counter
                        SET last_number = last_number + 1
                        WHERE id = 1 AND last_number < 99999999999
                        RETURNING last_number;
                        """;

                    var value = await command.ExecuteScalarAsync(cancellationToken);
                    if (value is null or DBNull)
                    {
                        throw new InvalidOperationException(
                            "El consecutivo de tarifas de Pricing excedió el rango soportado."
                        );
                    }

                    consecutive = Convert.ToInt64(value, CultureInfo.InvariantCulture);
                }

                if (consecutive <= 0 || consecutive > MaximumConsecutive)
                {
                    throw new InvalidOperationException(
                        "El consecutivo de tarifas de Pricing excedió el rango soportado."
                    );
                }

                var digits = consecutive.ToString("D11", CultureInfo.InvariantCulture);
                var candidate = $"QUO-{digits[..5]}-{digits[5..]}";

                // Protección para códigos históricos o escrituras que ocurrieron
                // durante el despliegue de la nueva versión.
                var alreadyExists = await dbContext
                    .Set<RateHeader>()
                    .AsNoTracking()
                    .AnyAsync(rate => rate.RateCode == candidate, cancellationToken);

                if (!alreadyExists)
                {
                    return new RateCodeReservation(transaction, candidate, ownsTransaction);
                }
            }
        }
        catch
        {
            if (ownsTransaction) await transaction.DisposeAsync();
            throw;
        }
    }

    private sealed class RateCodeReservation(
        IDbContextTransaction transaction,
        string code,
        bool ownsTransaction
    ) : IRateCodeReservation
    {
        public string Code { get; } = code;

        public Task CommitAsync(CancellationToken cancellationToken = default) =>
            ownsTransaction ? transaction.CommitAsync(cancellationToken) : Task.CompletedTask;

        public ValueTask DisposeAsync() =>
            ownsTransaction ? transaction.DisposeAsync() : ValueTask.CompletedTask;
    }
}

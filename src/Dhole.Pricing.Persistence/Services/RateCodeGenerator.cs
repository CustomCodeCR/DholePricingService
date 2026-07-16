using System.Data;
using System.Globalization;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Persistence.Services;

public sealed class RateCodeGenerator(ServiceDbContext dbContext) : IRateCodeGenerator
{
    public async Task<long> GetNextAsync(CancellationToken cancellationToken = default)
    {
        var connection = dbContext.Database.GetDbConnection();
        var shouldCloseConnection = connection.State != ConnectionState.Open;

        if (shouldCloseConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT nextval('pricing.rate_code_sequence');";

            var value = await command.ExecuteScalarAsync(cancellationToken);

            if (value is null || value is DBNull)
            {
                throw new InvalidOperationException(
                    "No se pudo obtener el consecutivo de la tarifa."
                );
            }

            return Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }
        finally
        {
            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }
    }
}

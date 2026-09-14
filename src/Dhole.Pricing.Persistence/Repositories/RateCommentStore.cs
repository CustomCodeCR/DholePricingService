using System.Data;
using System.Data.Common;
using Dhole.Pricing.Application.Abstractions.Repositories;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Persistence.Repositories;

public sealed class RateCommentStore(ServiceDbContext dbContext) : IRateCommentStore
{
    public async Task<string?> GetAsync(
        Guid rateId,
        CancellationToken cancellationToken = default
    )
    {
        var connection = dbContext.Database.GetDbConnection();
        var closeConnection = connection.State != ConnectionState.Open;

        if (closeConnection)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT \"Comments\" FROM pricing.\"RateComments\" WHERE \"RateId\" = @rateId";
            AddParameter(command, "@rateId", rateId, DbType.Guid);

            var value = await command.ExecuteScalarAsync(cancellationToken);
            return value is null or DBNull ? null : Convert.ToString(value)?.Trim();
        }
        finally
        {
            if (closeConnection)
                await connection.CloseAsync();
        }
    }

    public async Task SetAsync(
        Guid rateId,
        string? comments,
        Guid? updatedBy,
        CancellationToken cancellationToken = default
    )
    {
        var normalized = string.IsNullOrWhiteSpace(comments) ? null : comments.Trim();
        if (normalized?.Length > 4000)
            throw new ArgumentException("Los comentarios de la tarifa no pueden superar 4000 caracteres.", nameof(comments));

        var connection = dbContext.Database.GetDbConnection();
        var closeConnection = connection.State != ConnectionState.Open;

        if (closeConnection)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO pricing."RateComments" ("RateId", "Comments", "UpdatedAtUtc", "UpdatedBy")
                VALUES (@rateId, @comments, @updatedAtUtc, @updatedBy)
                ON CONFLICT ("RateId") DO UPDATE
                SET "Comments" = EXCLUDED."Comments",
                    "UpdatedAtUtc" = EXCLUDED."UpdatedAtUtc",
                    "UpdatedBy" = EXCLUDED."UpdatedBy";
                """;

            AddParameter(command, "@rateId", rateId, DbType.Guid);
            AddParameter(command, "@comments", normalized, DbType.String);
            AddParameter(command, "@updatedAtUtc", DateTime.UtcNow, DbType.DateTime);
            AddParameter(command, "@updatedBy", updatedBy, DbType.Guid);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            if (closeConnection)
                await connection.CloseAsync();
        }
    }

    private static void AddParameter(
        DbCommand command,
        string name,
        object? value,
        DbType dbType
    )
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = dbType;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}

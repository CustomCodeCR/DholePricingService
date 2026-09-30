using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Persistence.DbContexts;

public static class DatabaseMigrationCoordinator
{
    private const long AdvisoryLockKey = 742_026_093_000_001L;
    private static readonly TimeSpan MigrationCommandTimeout = TimeSpan.FromMinutes(5);

    public static async Task MigrateAsync(
        ServiceDbContext dbContext,
        CancellationToken cancellationToken = default
    )
    {
        dbContext.Database.SetCommandTimeout(MigrationCommandTimeout);
        await dbContext.Database.OpenConnectionAsync(cancellationToken);

        var connection = dbContext.Database.GetDbConnection();

        try
        {
            await using (var lockCommand = connection.CreateCommand())
            {
                lockCommand.CommandText = $"SELECT pg_advisory_lock({AdvisoryLockKey});";
                lockCommand.CommandTimeout = (int)MigrationCommandTimeout.TotalSeconds;
                await lockCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            try
            {
                // MigrateAsync re-reads __EFMigrationsHistory after the lock is acquired,
                // so API and Worker cannot race while registering the same migration.
                await dbContext.Database.MigrateAsync(cancellationToken);
            }
            finally
            {
                await using var unlockCommand = connection.CreateCommand();
                unlockCommand.CommandText = $"SELECT pg_advisory_unlock({AdvisoryLockKey});";
                unlockCommand.CommandTimeout = 30;
                await unlockCommand.ExecuteNonQueryAsync(CancellationToken.None);
            }
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }
}

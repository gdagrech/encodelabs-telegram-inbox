using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace EncodeLabs.Infrastructure.Persistence;

/// <summary>
/// Applies pending EF Core migrations at start-up, retrying while PostgreSQL is still accepting
/// connections. Compose orders containers, but ordering alone does not guarantee readiness.
/// </summary>
public sealed class InboxDatabaseInitialiser(
    InboxDbContext dbContext,
    IOptions<DatabaseOptions> options,
    TimeProvider timeProvider,
    ILogger<InboxDatabaseInitialiser> logger)
{
    private readonly DatabaseOptions options = options.Value;

    public async Task ApplyMigrationsAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset giveUpAtUtc = timeProvider.GetUtcNow().Add(options.MigrationTimeout);
        int attemptNumber = 0;

        while (true)
        {
            attemptNumber++;
            try
            {
                await dbContext.Database.MigrateAsync(cancellationToken);
                logger.LogInformation("Database schema is up to date (attempt {AttemptNumber}).", attemptNumber);
                return;
            }
            catch (Exception migrationException)
                when (IsDatabaseNotReadyYet(migrationException) && timeProvider.GetUtcNow() < giveUpAtUtc)
            {
                logger.LogWarning(
                    "PostgreSQL is not ready yet (attempt {AttemptNumber}): {FailureReason}. Retrying in {RetryDelaySeconds}s.",
                    attemptNumber,
                    migrationException.Message,
                    options.MigrationRetryDelay.TotalSeconds);

                await Task.Delay(options.MigrationRetryDelay, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Recognises "PostgreSQL is still starting" in any of its shapes. The connection failure is
    /// usually wrapped — by EF Core's retrying execution strategy, or by Npgsql around a socket
    /// error — so the whole chain is inspected rather than just the outermost exception.
    /// </summary>
    private static bool IsDatabaseNotReadyYet(Exception thrownException)
    {
        for (Exception? candidate = thrownException; candidate is not null; candidate = candidate.InnerException)
        {
            if (candidate is NpgsqlException or SocketException or TimeoutException)
            {
                return true;
            }
        }

        return false;
    }
}

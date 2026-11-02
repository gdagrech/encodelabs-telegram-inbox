using EncodeLabs.Application.Abstractions.Persistence;
using EncodeLabs.Domain.Messages;
using EncodeLabs.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EncodeLabs.Infrastructure.Persistence;

public sealed class UnitOfWork(InboxDbContext dbContext) : IUnitOfWork
{
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException dbUpdateException) when (IsDuplicateTelegramMessage(dbUpdateException))
        {
            throw new DuplicateTelegramMessageException(
                ExtractTelegramMessageId(dbUpdateException),
                dbUpdateException);
        }
    }

    private static bool IsDuplicateTelegramMessage(DbUpdateException dbUpdateException) =>
        dbUpdateException.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: MessageConfiguration.UniqueTelegramMessageIndexName
        };

    private static long ExtractTelegramMessageId(DbUpdateException dbUpdateException) =>
        dbUpdateException.Entries
            .Select(entry => entry.Entity)
            .OfType<Message>()
            .Select(message => message.TelegramMessageId)
            .FirstOrDefault();
}

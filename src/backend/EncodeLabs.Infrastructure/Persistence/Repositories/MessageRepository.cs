using EncodeLabs.Application.Abstractions.Persistence;
using EncodeLabs.Domain.Messages;
using Microsoft.EntityFrameworkCore;

namespace EncodeLabs.Infrastructure.Persistence.Repositories;

public sealed class MessageRepository(InboxDbContext dbContext) : IMessageRepository
{
    public void Add(Message message) => dbContext.Messages.Add(message);

    public Task<bool> ExistsForTelegramMessageIdAsync(
        Guid contactId,
        long telegramMessageId,
        CancellationToken cancellationToken) =>
        dbContext.Messages.AnyAsync(
            message => message.ContactId == contactId && message.TelegramMessageId == telegramMessageId,
            cancellationToken);

    public async Task<IReadOnlyList<Message>> ListByContactChronologicallyAsync(
        Guid contactId,
        CancellationToken cancellationToken) =>
        await dbContext.Messages
            .AsNoTracking()
            .Where(message => message.ContactId == contactId)
            .OrderBy(message => message.SequenceNumber)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Message>> ListUnreadIncomingUpToSequenceAsync(
        Guid contactId,
        long readUpToSequenceNumber,
        CancellationToken cancellationToken) =>
        await dbContext.Messages
            .Where(message =>
                message.ContactId == contactId
                && message.Direction == MessageDirection.Incoming
                && !message.IsRead
                && message.SequenceNumber <= readUpToSequenceNumber)
            .ToListAsync(cancellationToken);

    public Task<int> CountUnreadIncomingAsync(Guid contactId, CancellationToken cancellationToken) =>
        dbContext.Messages.CountAsync(
            message =>
                message.ContactId == contactId
                && message.Direction == MessageDirection.Incoming
                && !message.IsRead,
            cancellationToken);
}

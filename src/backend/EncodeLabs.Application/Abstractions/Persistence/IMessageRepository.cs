using EncodeLabs.Domain.Messages;

namespace EncodeLabs.Application.Abstractions.Persistence;

public interface IMessageRepository
{
    void Add(Message message);

    Task<bool> ExistsForTelegramMessageIdAsync(
        Guid contactId,
        long telegramMessageId,
        CancellationToken cancellationToken);

    /// <summary>Whole conversation, oldest first. History pagination is explicitly out of scope.</summary>
    Task<IReadOnlyList<Message>> ListByContactChronologicallyAsync(
        Guid contactId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Loads the unread incoming messages at or below <paramref name="readUpToSequenceNumber"/> so the
    /// domain entity performs the state change. Messages above the cut-off are deliberately excluded.
    /// </summary>
    Task<IReadOnlyList<Message>> ListUnreadIncomingUpToSequenceAsync(
        Guid contactId,
        long readUpToSequenceNumber,
        CancellationToken cancellationToken);

    Task<int> CountUnreadIncomingAsync(Guid contactId, CancellationToken cancellationToken);
}

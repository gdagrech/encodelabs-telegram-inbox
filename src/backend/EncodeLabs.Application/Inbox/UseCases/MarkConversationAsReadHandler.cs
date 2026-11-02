using EncodeLabs.Application.Abstractions.Persistence;
using EncodeLabs.Application.Abstractions.Realtime;
using EncodeLabs.Application.Inbox.ReadModels;
using EncodeLabs.Domain.Messages;
using Microsoft.Extensions.Logging;

namespace EncodeLabs.Application.Inbox.UseCases;

/// <summary>
/// Marks as read every incoming message the operator has actually received, up to and including the
/// given sequence number. Anything that arrives while this runs gets a higher sequence number and
/// therefore stays unread.
/// </summary>
public sealed class MarkConversationAsReadHandler(
    IContactRepository contactRepository,
    IMessageRepository messageRepository,
    IUnitOfWork unitOfWork,
    IInboxRealtimeNotifier inboxRealtimeNotifier,
    TimeProvider timeProvider,
    ILogger<MarkConversationAsReadHandler> logger)
{
    public async Task<ConversationReadState> HandleAsync(
        Guid contactId,
        long readUpToSequenceNumber,
        CancellationToken cancellationToken)
    {
        bool contactExists = await contactRepository.FindByIdAsync(contactId, cancellationToken) is not null;
        if (!contactExists)
        {
            throw new ContactNotFoundException(contactId);
        }

        IReadOnlyList<Message> messagesToMark = await messageRepository.ListUnreadIncomingUpToSequenceAsync(
            contactId,
            readUpToSequenceNumber,
            cancellationToken);

        DateTimeOffset readAtUtc = timeProvider.GetUtcNow();
        int markedMessageCount = messagesToMark.Count(message => message.MarkAsRead(readAtUtc));

        if (markedMessageCount > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        int remainingUnreadMessageCount =
            await messageRepository.CountUnreadIncomingAsync(contactId, cancellationToken);

        ConversationReadState conversationReadState =
            new(contactId, remainingUnreadMessageCount, readUpToSequenceNumber);

        if (markedMessageCount > 0)
        {
            logger.LogInformation(
                "Marked {MarkedMessageCount} incoming message(s) as read for contact {ContactId} up to sequence {ReadUpToSequenceNumber}; {RemainingUnreadMessageCount} still unread.",
                markedMessageCount,
                contactId,
                readUpToSequenceNumber,
                remainingUnreadMessageCount);

            await inboxRealtimeNotifier.NotifyConversationReadStateChangedAsync(
                conversationReadState,
                cancellationToken);
        }

        return conversationReadState;
    }
}

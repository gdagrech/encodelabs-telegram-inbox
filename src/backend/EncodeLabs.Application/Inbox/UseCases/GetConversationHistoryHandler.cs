using EncodeLabs.Application.Abstractions.Persistence;
using EncodeLabs.Application.Inbox.ReadModels;
using EncodeLabs.Domain.Contacts;
using EncodeLabs.Domain.Messages;

namespace EncodeLabs.Application.Inbox.UseCases;

/// <summary>
/// Returns the stored history of one conversation. Reading never changes the read state: marking as
/// read is a separate, explicit operation so the cut-off can be controlled by the client.
/// </summary>
public sealed class GetConversationHistoryHandler(
    IContactRepository contactRepository,
    IMessageRepository messageRepository)
{
    public async Task<ConversationHistory> HandleAsync(Guid contactId, CancellationToken cancellationToken)
    {
        Contact contact = await contactRepository.FindByIdAsync(contactId, cancellationToken)
            ?? throw new ContactNotFoundException(contactId);

        IReadOnlyList<Message> storedMessages =
            await messageRepository.ListByContactChronologicallyAsync(contactId, cancellationToken);

        List<MessageView> messageViews = [.. storedMessages.Select(MessageView.FromMessage)];
        long highestSequenceNumber = messageViews.Count == 0
            ? 0
            : messageViews.Max(messageView => messageView.SequenceNumber);

        return new ConversationHistory(
            contact.Id,
            contact.DisplayName,
            contact.Username,
            messageViews,
            highestSequenceNumber);
    }
}

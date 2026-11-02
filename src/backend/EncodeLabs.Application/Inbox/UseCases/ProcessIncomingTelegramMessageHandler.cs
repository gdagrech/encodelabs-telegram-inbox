using EncodeLabs.Application.Abstractions.Messaging;
using EncodeLabs.Application.Abstractions.Persistence;
using EncodeLabs.Application.Abstractions.Realtime;
using EncodeLabs.Application.Inbox.ReadModels;
using EncodeLabs.Domain.Contacts;
using EncodeLabs.Domain.Messages;
using Microsoft.Extensions.Logging;

namespace EncodeLabs.Application.Inbox.UseCases;

/// <summary>
/// Turns one queued Telegram message into persisted state and a realtime notification.
/// </summary>
/// <remarks>
/// The handler is idempotent because Telegram and RabbitMQ both deliver at least once. Duplicates are
/// detected twice: cheaply with a lookup, and authoritatively by the unique index on
/// (contact, Telegram message id) in case two deliveries are processed concurrently.
/// </remarks>
public sealed class ProcessIncomingTelegramMessageHandler(
    IContactRepository contactRepository,
    IMessageRepository messageRepository,
    IUnitOfWork unitOfWork,
    IInboxRealtimeNotifier inboxRealtimeNotifier,
    ILogger<ProcessIncomingTelegramMessageHandler> logger)
{
    public async Task<IncomingMessageProcessingOutcome> HandleAsync(
        IncomingTelegramMessage incomingTelegramMessage,
        CancellationToken cancellationToken)
    {
        MessageText messageText = MessageText.Create(incomingTelegramMessage.Text);

        Contact contact = await ResolveContactAsync(incomingTelegramMessage, cancellationToken);

        bool messageAlreadyStored = await messageRepository.ExistsForTelegramMessageIdAsync(
            contact.Id,
            incomingTelegramMessage.TelegramMessageId,
            cancellationToken);

        if (messageAlreadyStored)
        {
            logger.LogInformation(
                "Discarded duplicate delivery of Telegram message {TelegramMessageId} (update {UpdateId}) for contact {ContactId}.",
                incomingTelegramMessage.TelegramMessageId,
                incomingTelegramMessage.UpdateId,
                contact.Id);

            return IncomingMessageProcessingOutcome.DuplicateDiscarded;
        }

        Message incomingMessage = Message.CreateIncoming(
            contact.Id,
            incomingTelegramMessage.TelegramMessageId,
            messageText,
            incomingTelegramMessage.SentAtUtc);

        messageRepository.Add(incomingMessage);
        contact.RegisterActivity(incomingTelegramMessage.SentAtUtc);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DuplicateTelegramMessageException duplicateTelegramMessageException)
        {
            logger.LogInformation(
                duplicateTelegramMessageException,
                "Concurrent delivery of Telegram message {TelegramMessageId} (update {UpdateId}) was rejected by the unique index.",
                incomingTelegramMessage.TelegramMessageId,
                incomingTelegramMessage.UpdateId);

            return IncomingMessageProcessingOutcome.DuplicateDiscarded;
        }

        logger.LogInformation(
            "Stored incoming message {MessageId} (Telegram message {TelegramMessageId}) for contact {ContactId}.",
            incomingMessage.Id,
            incomingMessage.TelegramMessageId,
            contact.Id);

        await NotifyConnectedInboxesAsync(contact.Id, incomingMessage, cancellationToken);

        return IncomingMessageProcessingOutcome.Stored;
    }

    private async Task<Contact> ResolveContactAsync(
        IncomingTelegramMessage incomingTelegramMessage,
        CancellationToken cancellationToken)
    {
        Contact? existingContact = await contactRepository.FindByTelegramUserIdAsync(
            incomingTelegramMessage.TelegramUserId,
            cancellationToken);

        if (existingContact is not null)
        {
            existingContact.RefreshTelegramProfile(
                incomingTelegramMessage.FirstName,
                incomingTelegramMessage.LastName,
                incomingTelegramMessage.Username,
                incomingTelegramMessage.TelegramChatId);

            return existingContact;
        }

        Contact newContact = Contact.CreateFromTelegramSender(
            incomingTelegramMessage.TelegramUserId,
            incomingTelegramMessage.TelegramChatId,
            incomingTelegramMessage.FirstName,
            incomingTelegramMessage.LastName,
            incomingTelegramMessage.Username,
            incomingTelegramMessage.SentAtUtc);

        contactRepository.Add(newContact);

        logger.LogInformation(
            "Registered new contact {ContactId} for Telegram user {TelegramUserId}.",
            newContact.Id,
            newContact.TelegramUserId);

        return newContact;
    }

    private async Task NotifyConnectedInboxesAsync(
        Guid contactId,
        Message storedMessage,
        CancellationToken cancellationToken)
    {
        ContactSummary? contactSummary = await contactRepository.GetSummaryAsync(contactId, cancellationToken);
        if (contactSummary is null)
        {
            return;
        }

        await inboxRealtimeNotifier.NotifyMessageAddedAsync(
            contactSummary,
            MessageView.FromMessage(storedMessage),
            cancellationToken);
    }
}

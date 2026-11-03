using EncodeLabs.Application.Abstractions.Persistence;
using EncodeLabs.Application.Abstractions.Realtime;
using EncodeLabs.Application.Abstractions.Telegram;
using EncodeLabs.Application.Inbox.ReadModels;
using EncodeLabs.Domain.Contacts;
using EncodeLabs.Domain.Messages;
using Microsoft.Extensions.Logging;

namespace EncodeLabs.Application.Inbox.UseCases;

/// <summary>
/// Sends an operator reply to Telegram and stores it only after Telegram has acknowledged it, so the
/// inbox cannot display a reply that was never delivered.
/// </summary>
public sealed class SendOperatorReplyHandler(
    IContactRepository contactRepository,
    IMessageRepository messageRepository,
    IUnitOfWork unitOfWork,
    ITelegramReplySender telegramReplySender,
    IInboxRealtimeNotifier inboxRealtimeNotifier,
    ILogger<SendOperatorReplyHandler> logger)
{
    public async Task<MessageView> HandleAsync(
        Guid contactId,
        string replyText,
        CancellationToken cancellationToken)
    {
        Contact contact = await contactRepository.FindByIdAsync(contactId, cancellationToken)
            ?? throw new ContactNotFoundException(contactId);

        MessageText validatedReplyText = MessageText.Create(replyText);

        TelegramReplyDelivery telegramReplyDelivery = await telegramReplySender.SendTextMessageAsync(
            contact.TelegramChatId,
            validatedReplyText.Value,
            cancellationToken);

        Message outgoingMessage = Message.CreateOutgoing(
            contact.Id,
            telegramReplyDelivery.TelegramMessageId,
            validatedReplyText,
            telegramReplyDelivery.SentAtUtc);

        messageRepository.Add(outgoingMessage);
        contact.RegisterActivity(telegramReplyDelivery.SentAtUtc);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception persistenceException)
        {
            // Telegram already delivered this text; only our record of it is missing. Reporting a
            // plain failure here would invite the operator to send it again, and the contact would
            // receive it twice. The distinct exception lets the interface say "do not retry".
            logger.LogError(
                persistenceException,
                "Reply delivered to Telegram (message {TelegramMessageId}) for contact {ContactId} "
                    + "could not be stored. The contact received it; the inbox has no record of it.",
                telegramReplyDelivery.TelegramMessageId,
                contact.Id);

            throw new ReplyDeliveredButNotRecordedException(
                telegramReplyDelivery.TelegramMessageId,
                persistenceException);
        }

        logger.LogInformation(
            "Stored outgoing message {MessageId} (Telegram message {TelegramMessageId}) for contact {ContactId}.",
            outgoingMessage.Id,
            outgoingMessage.TelegramMessageId,
            contact.Id);

        MessageView messageView = MessageView.FromMessage(outgoingMessage);
        ContactSummary? contactSummary = await contactRepository.GetSummaryAsync(contact.Id, cancellationToken);
        if (contactSummary is not null)
        {
            await inboxRealtimeNotifier.NotifyMessageAddedAsync(contactSummary, messageView, cancellationToken);
        }

        return messageView;
    }
}

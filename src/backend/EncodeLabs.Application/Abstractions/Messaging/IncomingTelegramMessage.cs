namespace EncodeLabs.Application.Abstractions.Messaging;

/// <summary>
/// Queue contract for one accepted Telegram text message. This is our own type on purpose: the
/// payload stored in RabbitMQ must stay stable and readable independently of the Telegram SDK.
/// </summary>
/// <param name="UpdateId">Telegram <c>update_id</c>, carried for traceability in logs.</param>
/// <param name="TelegramMessageId">Telegram <c>message.message_id</c>, unique within the chat.</param>
/// <param name="TelegramUserId">Telegram <c>message.from.id</c>, the contact identity.</param>
/// <param name="TelegramChatId">Telegram <c>message.chat.id</c>, the reply destination.</param>
public sealed record IncomingTelegramMessage(
    long UpdateId,
    long TelegramMessageId,
    long TelegramUserId,
    long TelegramChatId,
    string? FirstName,
    string? LastName,
    string? Username,
    string Text,
    DateTimeOffset SentAtUtc);

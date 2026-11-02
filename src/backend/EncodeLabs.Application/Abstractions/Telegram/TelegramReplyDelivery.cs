namespace EncodeLabs.Application.Abstractions.Telegram;

/// <summary>
/// Acknowledgement returned by Telegram for a delivered reply.
/// </summary>
public sealed record TelegramReplyDelivery(long TelegramMessageId, DateTimeOffset SentAtUtc);

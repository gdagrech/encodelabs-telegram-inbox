namespace EncodeLabs.Application.Abstractions.Persistence;

/// <summary>
/// Signals that a Telegram message already exists in the conversation. Delivery is at-least-once, so
/// this is an expected condition rather than a defect: the processing pipeline treats it as success.
/// </summary>
public sealed class DuplicateTelegramMessageException(long telegramMessageId, Exception innerException)
    : Exception($"Telegram message {telegramMessageId} is already stored for this conversation.", innerException)
{
    public long TelegramMessageId { get; } = telegramMessageId;
}

namespace EncodeLabs.Application.Abstractions.Telegram;

/// <summary>
/// Signals that the Telegram Bot API rejected or could not be reached for an outgoing reply.
/// </summary>
public sealed class TelegramReplyFailedException(string message, Exception? innerException = null)
    : Exception(message, innerException);

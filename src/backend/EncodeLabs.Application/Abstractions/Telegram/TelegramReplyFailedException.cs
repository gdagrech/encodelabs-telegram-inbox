namespace EncodeLabs.Application.Abstractions.Telegram;

/// <summary>
/// Signals that the Telegram Bot API rejected or could not be reached for an outgoing reply.
/// </summary>
public sealed class TelegramReplyFailedException(
    string message,
    string? telegramDescription = null,
    Exception? innerException = null)
    : Exception(message, innerException)
{
    /// <summary>
    /// Telegram's own explanation of the rejection, such as <c>Forbidden: bot was blocked by the
    /// user</c>. Null when Telegram never answered, which is a different failure for the operator:
    /// a rejection will not succeed on retry, an unreachable API may.
    /// </summary>
    public string? TelegramDescription { get; } = telegramDescription;
}

namespace EncodeLabs.Application.Abstractions.Telegram;

public interface ITelegramReplySender
{
    /// <summary>
    /// Delivers an operator reply through the Telegram Bot API.
    /// </summary>
    /// <returns>The identity Telegram assigned to the delivered message.</returns>
    /// <exception cref="TelegramReplyFailedException">
    /// Thrown when Telegram did not acknowledge the message. Nothing is stored in that case, so the
    /// inbox never shows a reply that the contact will not receive.
    /// </exception>
    Task<TelegramReplyDelivery> SendTextMessageAsync(
        long telegramChatId,
        string text,
        CancellationToken cancellationToken);
}

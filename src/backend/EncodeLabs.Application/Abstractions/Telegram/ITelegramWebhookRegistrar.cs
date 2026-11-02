namespace EncodeLabs.Application.Abstractions.Telegram;

public interface ITelegramWebhookRegistrar
{
    /// <summary>
    /// Points the bot's webhook at <paramref name="publicWebhookUrl"/> and configures the secret token
    /// Telegram must echo back in every delivery.
    /// </summary>
    /// <exception cref="TelegramWebhookRegistrationFailedException">Thrown when Telegram rejected the registration.</exception>
    Task RegisterWebhookAsync(Uri publicWebhookUrl, CancellationToken cancellationToken);

    /// <summary>
    /// Reads back the webhook Telegram currently has on record, for start-up verification and logging.
    /// Never includes the secret token.
    /// </summary>
    Task<TelegramWebhookStatus> GetWebhookStatusAsync(CancellationToken cancellationToken);
}

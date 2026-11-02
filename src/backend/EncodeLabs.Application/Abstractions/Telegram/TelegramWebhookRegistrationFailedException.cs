namespace EncodeLabs.Application.Abstractions.Telegram;

/// <summary>
/// Signals that Telegram did not accept the webhook registration.
/// </summary>
public sealed class TelegramWebhookRegistrationFailedException(string message, Exception? innerException = null)
    : Exception(message, innerException);

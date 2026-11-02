namespace EncodeLabs.Application.Abstractions.Telegram;

/// <summary>
/// Non-sensitive subset of Telegram's <c>getWebhookInfo</c> response.
/// </summary>
public sealed record TelegramWebhookStatus(
    string? RegisteredUrl,
    int PendingUpdateCount,
    string? LastErrorMessage,
    DateTimeOffset? LastErrorAtUtc);

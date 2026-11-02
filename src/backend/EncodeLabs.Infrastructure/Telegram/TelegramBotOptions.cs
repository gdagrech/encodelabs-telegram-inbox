using System.ComponentModel.DataAnnotations;

namespace EncodeLabs.Infrastructure.Telegram;

public sealed class TelegramBotOptions
{
    public const string ConfigurationSectionName = "Telegram";

    /// <summary>
    /// Token issued by BotFather, in its documented <c>&lt;bot id&gt;:&lt;secret&gt;</c> shape. Never
    /// logged and never returned by any endpoint.
    /// </summary>
    /// <remarks>
    /// The shape is validated here so a mistyped token fails at start-up with a readable message
    /// instead of surfacing as an exception from inside the Telegram SDK. The error message quotes
    /// the expected format, never the value.
    /// </remarks>
    [Required(AllowEmptyStrings = false)]
    [RegularExpression(
        @"^\d{5,}:[A-Za-z0-9_-]{30,}$",
        ErrorMessage = "TELEGRAM_BOT_TOKEN must look like 123456789:AA… — the value BotFather returns.")]
    public string BotToken { get; init; } = string.Empty;

    /// <summary>
    /// Value Telegram must echo in the <c>X-Telegram-Bot-Api-Secret-Token</c> header. It is the only
    /// thing that distinguishes a genuine delivery from an arbitrary internet request, because the
    /// webhook itself is publicly reachable.
    /// </summary>
    /// <remarks>
    /// Telegram only accepts 1-256 characters of <c>A-Z a-z 0-9 _ -</c> and rejects the whole
    /// <c>setWebhook</c> call otherwise, so the alphabet is enforced here rather than discovered
    /// during registration. A 16-character minimum is this system's own floor, not Telegram's.
    /// </remarks>
    [Required(AllowEmptyStrings = false)]
    [RegularExpression(
        "^[A-Za-z0-9_-]{16,256}$",
        ErrorMessage = "TELEGRAM_WEBHOOK_SECRET_TOKEN must be 16-256 characters of A-Z a-z 0-9 _ - (try: openssl rand -hex 32).")]
    public string WebhookSecretToken { get; init; } = string.Empty;

    /// <summary>Absolute path of the webhook endpoint, appended to the public tunnel address.</summary>
    [Required(AllowEmptyStrings = false)]
    public string WebhookPath { get; init; } = "/api/telegram/webhook";

    /// <summary>
    /// How many times start-up tries to discover the tunnel and register the webhook before giving up
    /// with an actionable error. Bounded so a misconfigured environment fails visibly instead of
    /// retrying in silence forever.
    /// </summary>
    [Range(1, 200)]
    public int WebhookRegistrationMaximumAttempts { get; init; } = 30;

    /// <summary>Pause between webhook registration attempts.</summary>
    public TimeSpan WebhookRegistrationRetryDelay { get; init; } = TimeSpan.FromSeconds(5);
}

using System.ComponentModel.DataAnnotations;

namespace EncodeLabs.Infrastructure.Telegram;

public sealed class TelegramBotOptions
{
    public const string ConfigurationSectionName = "Telegram";

    /// <summary>Token issued by BotFather. Never logged and never returned by any endpoint.</summary>
    [Required(AllowEmptyStrings = false)]
    public string BotToken { get; init; } = string.Empty;

    /// <summary>
    /// Value Telegram must echo in the <c>X-Telegram-Bot-Api-Secret-Token</c> header. It is the only
    /// thing that distinguishes a genuine delivery from an arbitrary internet request, because the
    /// webhook itself is publicly reachable.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    [MinLength(16)]
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

using System.Text.Json.Serialization;

namespace EncodeLabs.Api.Contracts;

/// <summary>
/// The subset of Telegram's <c>Update</c> object this inbox understands.
/// </summary>
/// <remarks>
/// These contracts are declared locally rather than taken from the Telegram SDK on purpose: the
/// webhook accepts a deliberately narrow shape (private text messages from humans) and everything it
/// accepts is re-published onto RabbitMQ under our own stable contract.
/// </remarks>
public sealed record TelegramWebhookPayload(
    [property: JsonPropertyName("update_id")] long UpdateId,
    [property: JsonPropertyName("message")] TelegramWebhookMessage? Message);

public sealed record TelegramWebhookMessage(
    [property: JsonPropertyName("message_id")] long MessageId,
    [property: JsonPropertyName("date")] long SentAtUnixSeconds,
    [property: JsonPropertyName("text")] string? Text,
    [property: JsonPropertyName("from")] TelegramWebhookSender? Sender,
    [property: JsonPropertyName("chat")] TelegramWebhookChat? Chat);

public sealed record TelegramWebhookSender(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("is_bot")] bool IsBot,
    [property: JsonPropertyName("first_name")] string? FirstName,
    [property: JsonPropertyName("last_name")] string? LastName,
    [property: JsonPropertyName("username")] string? Username);

public sealed record TelegramWebhookChat(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("type")] string? Type)
{
    /// <summary>Only one-to-one chats are in scope; groups and channels are ignored.</summary>
    public const string PrivateChatType = "private";
}

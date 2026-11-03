namespace EncodeLabs.Application.Abstractions.Telegram;

/// <summary>
/// Telegram accepted and delivered the reply, but storing it failed afterwards.
/// </summary>
/// <remarks>
/// This is the one failure the operator must not retry: the contact already received the text, so
/// sending it again delivers a duplicate. It is kept apart from <see cref="TelegramReplyFailedException"/>
/// precisely because the two demand opposite reactions — retry one, never the other.
/// </remarks>
public sealed class ReplyDeliveredButNotRecordedException(long telegramMessageId, Exception innerException)
    : Exception(
        $"The reply was delivered to Telegram (message {telegramMessageId}) but could not be stored.",
        innerException)
{
    /// <summary>The identifier Telegram assigned, so the delivery can be traced in the logs.</summary>
    public long TelegramMessageId { get; } = telegramMessageId;
}

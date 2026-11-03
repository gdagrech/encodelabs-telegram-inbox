using EncodeLabs.Domain.Messages;

namespace EncodeLabs.Application.Inbox.ReadModels;

/// <summary>
/// One row of the inbox contact list.
/// </summary>
public sealed record ContactSummary(
    Guid ContactId,
    string DisplayName,
    string? Username,
    int UnreadMessageCount,
    string? LastMessagePreview,
    MessageDirection? LastMessageDirection,
    DateTimeOffset LastActivityAtUtc)
{
    /// <summary>
    /// Previews are truncated on the server so the contact list stays small even when a contact sends
    /// a message close to Telegram's 4096-character limit.
    /// </summary>
    public const int MaximumPreviewLength = 160;

    /// <remarks>
    /// The cut is pulled back by one when it would land between the two halves of a surrogate pair.
    /// An emoji occupies two <see cref="char"/> values, and splitting it renders as the replacement
    /// character — a preview ending in "�" looks like a storage bug to the operator.
    /// </remarks>
    public static string BuildPreview(string messageText)
    {
        if (messageText.Length <= MaximumPreviewLength)
        {
            return messageText;
        }

        int cutLength = char.IsHighSurrogate(messageText[MaximumPreviewLength - 1])
            ? MaximumPreviewLength - 1
            : MaximumPreviewLength;

        return string.Concat(messageText.AsSpan(0, cutLength), "…");
    }
}

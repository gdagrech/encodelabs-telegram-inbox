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

    public static string BuildPreview(string messageText) =>
        messageText.Length <= MaximumPreviewLength
            ? messageText
            : string.Concat(messageText.AsSpan(0, MaximumPreviewLength), "…");
}

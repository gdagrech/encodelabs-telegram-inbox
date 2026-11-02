namespace EncodeLabs.Application.Inbox.ReadModels;

/// <summary>
/// Full message history of one conversation, oldest first.
/// </summary>
/// <param name="HighestSequenceNumber">
/// Largest sequence number contained in <paramref name="Messages"/>, or <c>0</c> for an empty
/// conversation. The client echoes this value back when marking the conversation as read, so only
/// messages it actually received are affected.
/// </param>
public sealed record ConversationHistory(
    Guid ContactId,
    string DisplayName,
    string? Username,
    IReadOnlyList<MessageView> Messages,
    long HighestSequenceNumber);

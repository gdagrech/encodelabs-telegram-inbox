namespace EncodeLabs.Application.Inbox.ReadModels;

/// <summary>
/// Outcome of marking a conversation as read, broadcast so every connected inbox agrees on the counters.
/// </summary>
/// <param name="ReadUpToSequenceNumber">
/// Inclusive cut-off that was applied. Messages above it stay unread, which is what keeps a message
/// arriving mid-operation from being silently marked as seen.
/// </param>
public sealed record ConversationReadState(
    Guid ContactId,
    int UnreadMessageCount,
    long ReadUpToSequenceNumber);

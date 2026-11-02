namespace EncodeLabs.Api.Contracts;

/// <summary>
/// Request to mark a conversation as read.
/// </summary>
/// <param name="ReadUpToSequenceNumber">
/// Highest sequence number the client has actually received, taken from the conversation history it
/// rendered. Messages above it keep their unread state, which is what stops a message that arrives
/// during the round trip from being marked as seen.
/// </param>
public sealed record MarkConversationAsReadRequest(long ReadUpToSequenceNumber);

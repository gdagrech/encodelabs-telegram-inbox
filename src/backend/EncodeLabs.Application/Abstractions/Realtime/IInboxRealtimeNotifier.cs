using EncodeLabs.Application.Inbox.ReadModels;

namespace EncodeLabs.Application.Abstractions.Realtime;

/// <summary>
/// Push channel towards every connected inbox. Implemented with SignalR in the API host.
/// </summary>
public interface IInboxRealtimeNotifier
{
    /// <summary>
    /// Announces a message that has already been committed, together with the refreshed contact list row.
    /// Always called after the database transaction so clients can never observe a message the
    /// database does not have.
    /// </summary>
    Task NotifyMessageAddedAsync(
        ContactSummary contactSummary,
        MessageView message,
        CancellationToken cancellationToken);

    Task NotifyConversationReadStateChangedAsync(
        ConversationReadState conversationReadState,
        CancellationToken cancellationToken);
}

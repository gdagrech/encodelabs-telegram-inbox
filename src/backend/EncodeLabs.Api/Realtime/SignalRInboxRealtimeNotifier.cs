using EncodeLabs.Application.Abstractions.Realtime;
using EncodeLabs.Application.Inbox.ReadModels;
using Microsoft.AspNetCore.SignalR;

namespace EncodeLabs.Api.Realtime;

/// <summary>
/// Broadcasts committed inbox changes over SignalR.
/// </summary>
/// <remarks>
/// A failed broadcast is logged but never propagated: the database transaction has already committed,
/// so failing the caller would roll nothing back and, for an inbound message, would turn an
/// acknowledged delivery into a redelivery. Clients close the resulting gap themselves by refetching
/// contacts and the open conversation whenever the connection is (re)established.
/// </remarks>
public sealed class SignalRInboxRealtimeNotifier(
    IHubContext<InboxHub> inboxHubContext,
    ILogger<SignalRInboxRealtimeNotifier> logger) : IInboxRealtimeNotifier
{
    public Task NotifyMessageAddedAsync(
        ContactSummary contactSummary,
        MessageView message,
        CancellationToken cancellationToken) =>
        BroadcastQuietlyAsync(
            InboxRealtimeEventNames.MessageAdded,
            new MessageAddedNotification(contactSummary, message),
            cancellationToken);

    public Task NotifyConversationReadStateChangedAsync(
        ConversationReadState conversationReadState,
        CancellationToken cancellationToken) =>
        BroadcastQuietlyAsync(
            InboxRealtimeEventNames.ConversationReadStateChanged,
            conversationReadState,
            cancellationToken);

    private async Task BroadcastQuietlyAsync(
        string eventName,
        object notificationPayload,
        CancellationToken cancellationToken)
    {
        try
        {
            await inboxHubContext.Clients.All.SendAsync(eventName, notificationPayload, cancellationToken);
        }
        catch (Exception broadcastException)
        {
            logger.LogWarning(
                broadcastException,
                "Broadcasting {EventName} failed. The change is already persisted; connected clients will reconcile on their next fetch.",
                eventName);
        }
    }

    /// <summary>Payload of <see cref="InboxRealtimeEventNames.MessageAdded"/>.</summary>
    private sealed record MessageAddedNotification(ContactSummary ContactSummary, MessageView Message);
}

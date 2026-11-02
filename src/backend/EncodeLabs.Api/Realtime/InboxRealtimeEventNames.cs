namespace EncodeLabs.Api.Realtime;

/// <summary>
/// Names of the hub events the Angular client subscribes to. Two events are enough: one for a new
/// message in either direction, one for a read-state change.
/// </summary>
public static class InboxRealtimeEventNames
{
    public const string MessageAdded = "MessageAdded";

    public const string ConversationReadStateChanged = "ConversationReadStateChanged";
}

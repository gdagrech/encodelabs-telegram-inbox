namespace EncodeLabs.Application.Inbox.UseCases;

/// <summary>
/// Result of processing one queued Telegram message. Both values are successes from the broker's
/// point of view and lead to an acknowledgement.
/// </summary>
public enum IncomingMessageProcessingOutcome
{
    /// <summary>The message was stored and connected clients were notified.</summary>
    Stored = 1,

    /// <summary>The message was already stored; a replayed delivery was discarded.</summary>
    DuplicateDiscarded = 2
}

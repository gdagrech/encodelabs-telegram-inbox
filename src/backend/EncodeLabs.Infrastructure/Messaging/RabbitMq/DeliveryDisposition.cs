namespace EncodeLabs.Infrastructure.Messaging.RabbitMq;

/// <summary>
/// What should happen to a delivery once processing stops.
/// </summary>
/// <remarks>
/// The three cases are deliberately distinct. A boolean used to carry this decision, and the two
/// failing cases collapsed into the same value: shutting down was treated as a verdict about the
/// message and dead-lettered it. Rejecting a message and merely declining to confirm it are opposite
/// intentions, so they are opposite values. See CR-018 in docs/code-review.md.
/// </remarks>
public enum DeliveryDisposition
{
    /// <summary>Processed successfully, or a duplicate that is already stored. Confirm it.</summary>
    Acknowledge,

    /// <summary>The message cannot be stored, now or ever. Route it to the dead-letter queue.</summary>
    DeadLetter,

    /// <summary>
    /// No verdict was reached, because the host is stopping. Neither confirm nor reject: leaving the
    /// delivery unconfirmed makes RabbitMQ requeue it when the channel closes.
    /// </summary>
    LeaveUnacknowledged
}

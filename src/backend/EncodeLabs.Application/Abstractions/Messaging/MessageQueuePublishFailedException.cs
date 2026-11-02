namespace EncodeLabs.Application.Abstractions.Messaging;

/// <summary>
/// Signals that a message could not be handed over to the broker with a publisher confirmation.
/// </summary>
public sealed class MessageQueuePublishFailedException(string message, Exception? innerException = null)
    : Exception(message, innerException);

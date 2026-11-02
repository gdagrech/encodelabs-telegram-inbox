namespace EncodeLabs.Application.Abstractions.Messaging;

public interface IIncomingMessageQueuePublisher
{
    /// <summary>
    /// Publishes an accepted Telegram message and returns only once the broker has confirmed it.
    /// </summary>
    /// <exception cref="MessageQueuePublishFailedException">
    /// Thrown when the broker did not confirm the publish. The webhook must then answer with a
    /// non-success status so Telegram retries instead of dropping the message.
    /// </exception>
    Task PublishConfirmedAsync(IncomingTelegramMessage incomingTelegramMessage, CancellationToken cancellationToken);
}

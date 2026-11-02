namespace EncodeLabs.Infrastructure.Messaging.RabbitMq;

/// <summary>
/// Names of the single exchange/queue pair used for inbound Telegram messages, plus its dead-letter
/// counterpart. Kept in one place so the publisher, the consumer and the documentation cannot drift.
/// </summary>
public static class IncomingMessageQueueTopology
{
    public const string ExchangeName = "encodelabs.inbox.telegram";

    public const string RoutingKey = "telegram.incoming-message";

    public const string QueueName = "encodelabs.inbox.telegram.incoming-messages";

    public const string DeadLetterExchangeName = "encodelabs.inbox.telegram.dead-letter";

    public const string DeadLetterQueueName = "encodelabs.inbox.telegram.incoming-messages.dead-letter";
}

using System.Text.Json;

namespace EncodeLabs.Infrastructure.Messaging.RabbitMq;

internal static class IncomingMessageQueueSerialization
{
    public const string ContentType = "application/json";

    /// <summary>
    /// Camel-case payloads so a queued message stays readable in the RabbitMQ management UI while an
    /// operator is diagnosing a dead-lettered delivery.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
}

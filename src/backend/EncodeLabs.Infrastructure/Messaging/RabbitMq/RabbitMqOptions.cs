using System.ComponentModel.DataAnnotations;

namespace EncodeLabs.Infrastructure.Messaging.RabbitMq;

public sealed class RabbitMqOptions
{
    public const string ConfigurationSectionName = "RabbitMq";

    /// <summary>AMQP URI, for example <c>amqp://user:password@rabbitmq:5672/</c>.</summary>
    [Required(AllowEmptyStrings = false)]
    public string ConnectionString { get; init; } = string.Empty;

    /// <summary>How many unacknowledged messages the consumer accepts at once.</summary>
    [Range(1, 1000)]
    public ushort PrefetchCount { get; init; } = 10;

    /// <summary>
    /// Processing attempts per delivery before the message is dead-lettered. Bounding this is what
    /// prevents a poison message from looping forever.
    /// </summary>
    [Range(1, 10)]
    public int MaximumProcessingAttempts { get; init; } = 3;

    /// <summary>Base back-off between processing attempts; grows linearly with the attempt number.</summary>
    public TimeSpan ProcessingRetryDelay { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Delay before the consumer rebuilds its channel after a broker connection drops.</summary>
    public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromSeconds(5);
}

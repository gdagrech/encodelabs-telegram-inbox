using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace EncodeLabs.Infrastructure.Messaging.RabbitMq;

/// <summary>
/// Owns the single AMQP connection shared by the publisher and the consumer, and declares the
/// durable topology. Connecting is deferred until first use so the API can start and serve health
/// checks while the broker is still booting.
/// </summary>
public sealed class RabbitMqConnectionProvider : IAsyncDisposable
{
    private readonly RabbitMqOptions options;
    private readonly ILogger<RabbitMqConnectionProvider> logger;
    private readonly SemaphoreSlim connectionInitialisationGate = new(1, 1);

    private IConnection? currentConnection;
    private bool topologyDeclared;

    public RabbitMqConnectionProvider(
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqConnectionProvider> logger)
    {
        this.options = options.Value;
        this.logger = logger;
    }

    public async Task<IConnection> GetOpenConnectionAsync(CancellationToken cancellationToken)
    {
        IConnection? establishedConnection = currentConnection;
        if (establishedConnection is { IsOpen: true })
        {
            return establishedConnection;
        }

        await connectionInitialisationGate.WaitAsync(cancellationToken);
        try
        {
            if (currentConnection is { IsOpen: true })
            {
                return currentConnection;
            }

            if (currentConnection is not null)
            {
                await DisposeConnectionQuietlyAsync(currentConnection);
                currentConnection = null;
                topologyDeclared = false;
            }

            ConnectionFactory connectionFactory = new()
            {
                Uri = new Uri(options.ConnectionString),
                ClientProvidedName = "encodelabs-inbox-api",
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true,
                NetworkRecoveryInterval = options.ReconnectDelay
            };

            currentConnection = await connectionFactory.CreateConnectionAsync(cancellationToken);
            logger.LogInformation("Connected to RabbitMQ at {RabbitMqEndpoint}.", DescribeEndpoint(currentConnection));

            return currentConnection;
        }
        finally
        {
            connectionInitialisationGate.Release();
        }
    }

    /// <summary>
    /// Declares the durable exchange, queue, bindings and dead-letter counterpart. Declarations are
    /// idempotent, so running this on every start-up and after a reconnect is safe.
    /// </summary>
    public async Task EnsureTopologyDeclaredAsync(CancellationToken cancellationToken)
    {
        if (topologyDeclared)
        {
            return;
        }

        IConnection connection = await GetOpenConnectionAsync(cancellationToken);
        await using IChannel topologyChannel = await connection.CreateChannelAsync(
            cancellationToken: cancellationToken);

        await topologyChannel.ExchangeDeclareAsync(
            IncomingMessageQueueTopology.DeadLetterExchangeName,
            ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);

        await topologyChannel.QueueDeclareAsync(
            IncomingMessageQueueTopology.DeadLetterQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: cancellationToken);

        await topologyChannel.QueueBindAsync(
            IncomingMessageQueueTopology.DeadLetterQueueName,
            IncomingMessageQueueTopology.DeadLetterExchangeName,
            IncomingMessageQueueTopology.RoutingKey,
            cancellationToken: cancellationToken);

        await topologyChannel.ExchangeDeclareAsync(
            IncomingMessageQueueTopology.ExchangeName,
            ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);

        Dictionary<string, object?> queueArguments = new()
        {
            ["x-dead-letter-exchange"] = IncomingMessageQueueTopology.DeadLetterExchangeName,
            ["x-dead-letter-routing-key"] = IncomingMessageQueueTopology.RoutingKey
        };

        await topologyChannel.QueueDeclareAsync(
            IncomingMessageQueueTopology.QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: queueArguments,
            cancellationToken: cancellationToken);

        await topologyChannel.QueueBindAsync(
            IncomingMessageQueueTopology.QueueName,
            IncomingMessageQueueTopology.ExchangeName,
            IncomingMessageQueueTopology.RoutingKey,
            cancellationToken: cancellationToken);

        topologyDeclared = true;
        logger.LogInformation(
            "Declared durable queue {QueueName} bound to {ExchangeName} with dead-letter queue {DeadLetterQueueName}.",
            IncomingMessageQueueTopology.QueueName,
            IncomingMessageQueueTopology.ExchangeName,
            IncomingMessageQueueTopology.DeadLetterQueueName);
    }

    public async ValueTask DisposeAsync()
    {
        if (currentConnection is not null)
        {
            await DisposeConnectionQuietlyAsync(currentConnection);
            currentConnection = null;
        }

        connectionInitialisationGate.Dispose();
    }

    private async Task DisposeConnectionQuietlyAsync(IConnection connection)
    {
        try
        {
            await connection.DisposeAsync();
        }
        catch (Exception connectionDisposeException)
        {
            logger.LogWarning(
                connectionDisposeException,
                "Closing the previous RabbitMQ connection failed; a new connection will be opened regardless.");
        }
    }

    private static string DescribeEndpoint(IConnection connection) =>
        $"{connection.Endpoint.HostName}:{connection.Endpoint.Port}";
}

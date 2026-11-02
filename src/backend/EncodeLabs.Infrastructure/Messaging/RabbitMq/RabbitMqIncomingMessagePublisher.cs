using System.Text.Json;
using EncodeLabs.Application.Abstractions.Messaging;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace EncodeLabs.Infrastructure.Messaging.RabbitMq;

/// <summary>
/// Publishes inbound Telegram messages with publisher confirmations enabled. The publish only
/// completes once the broker has confirmed the message was accepted into the durable queue, which is
/// what allows the webhook to answer Telegram truthfully.
/// </summary>
public sealed class RabbitMqIncomingMessagePublisher(
    RabbitMqConnectionProvider connectionProvider,
    ILogger<RabbitMqIncomingMessagePublisher> logger) : IIncomingMessageQueuePublisher, IAsyncDisposable
{
    // An AMQP channel must not be used concurrently, and one confirm-tracking channel is plenty for
    // webhook traffic, so publishes are serialised rather than spread over a channel pool.
    private readonly SemaphoreSlim publishGate = new(1, 1);

    private IChannel? confirmingChannel;

    public async Task PublishConfirmedAsync(
        IncomingTelegramMessage incomingTelegramMessage,
        CancellationToken cancellationToken)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(
            incomingTelegramMessage,
            IncomingMessageQueueSerialization.Options);

        BasicProperties messageProperties = new()
        {
            ContentType = IncomingMessageQueueSerialization.ContentType,
            DeliveryMode = DeliveryModes.Persistent,
            MessageId = incomingTelegramMessage.UpdateId.ToString(),
            Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        };

        await publishGate.WaitAsync(cancellationToken);
        try
        {
            IChannel channel = await GetConfirmingChannelAsync(cancellationToken);

            await channel.BasicPublishAsync(
                exchange: IncomingMessageQueueTopology.ExchangeName,
                routingKey: IncomingMessageQueueTopology.RoutingKey,
                mandatory: true,
                basicProperties: messageProperties,
                body: payload,
                cancellationToken: cancellationToken);

            logger.LogInformation(
                "Queued Telegram update {UpdateId} (message {TelegramMessageId}) for processing.",
                incomingTelegramMessage.UpdateId,
                incomingTelegramMessage.TelegramMessageId);
        }
        catch (Exception publishException) when (publishException is not OperationCanceledException)
        {
            // Drop the channel so the next publish rebuilds it instead of reusing a broken one.
            await DisposeChannelQuietlyAsync();

            throw new MessageQueuePublishFailedException(
                $"RabbitMQ did not confirm Telegram update {incomingTelegramMessage.UpdateId}.",
                publishException);
        }
        finally
        {
            publishGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeChannelQuietlyAsync();
        publishGate.Dispose();
    }

    private async Task<IChannel> GetConfirmingChannelAsync(CancellationToken cancellationToken)
    {
        if (confirmingChannel is { IsOpen: true })
        {
            return confirmingChannel;
        }

        await DisposeChannelQuietlyAsync();
        await connectionProvider.EnsureTopologyDeclaredAsync(cancellationToken);

        IConnection connection = await connectionProvider.GetOpenConnectionAsync(cancellationToken);
        confirmingChannel = await connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true),
            cancellationToken);

        // An unroutable message would otherwise be silently discarded despite being "confirmed".
        confirmingChannel.BasicReturnAsync += (_, returnedMessage) =>
        {
            logger.LogError(
                "RabbitMQ returned a message as unroutable: {ReplyCode} {ReplyText} (exchange {Exchange}, routing key {RoutingKey}).",
                returnedMessage.ReplyCode,
                returnedMessage.ReplyText,
                returnedMessage.Exchange,
                returnedMessage.RoutingKey);

            return Task.CompletedTask;
        };

        return confirmingChannel;
    }

    private async Task DisposeChannelQuietlyAsync()
    {
        if (confirmingChannel is null)
        {
            return;
        }

        try
        {
            await confirmingChannel.DisposeAsync();
        }
        catch (Exception channelDisposeException) when (channelDisposeException is OperationInterruptedException or IOException)
        {
            logger.LogWarning(
                channelDisposeException,
                "Closing the RabbitMQ publisher channel failed; a new channel will be created on the next publish.");
        }
        finally
        {
            confirmingChannel = null;
        }
    }
}

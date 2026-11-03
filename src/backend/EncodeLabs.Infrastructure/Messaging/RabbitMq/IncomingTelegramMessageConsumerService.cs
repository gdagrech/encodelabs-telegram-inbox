using System.Text.Json;
using EncodeLabs.Application.Abstractions.Messaging;
using EncodeLabs.Application.Inbox.UseCases;
using EncodeLabs.Domain.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace EncodeLabs.Infrastructure.Messaging.RabbitMq;

/// <summary>
/// Consumes inbound Telegram messages from the durable queue and hands each one to the application
/// use case. Deliveries are acknowledged manually, only after the database transaction has committed,
/// so a crash mid-processing results in redelivery rather than a lost message.
/// </summary>
public sealed class IncomingTelegramMessageConsumerService(
    RabbitMqConnectionProvider connectionProvider,
    IServiceScopeFactory serviceScopeFactory,
    IOptions<RabbitMqOptions> options,
    ILogger<IncomingTelegramMessageConsumerService> logger) : BackgroundService
{
    private readonly RabbitMqOptions options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeUntilChannelClosesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception consumerException)
            {
                logger.LogError(
                    consumerException,
                    "The Telegram message consumer stopped unexpectedly. Retrying in {ReconnectDelaySeconds}s.",
                    options.ReconnectDelay.TotalSeconds);
            }

            if (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(options.ReconnectDelay, stoppingToken);
            }
        }
    }

    private async Task ConsumeUntilChannelClosesAsync(CancellationToken stoppingToken)
    {
        await connectionProvider.EnsureTopologyDeclaredAsync(stoppingToken);
        IConnection connection = await connectionProvider.GetOpenConnectionAsync(stoppingToken);

        await using IChannel consumerChannel = await connection.CreateChannelAsync(
            cancellationToken: stoppingToken);

        await consumerChannel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: options.PrefetchCount,
            global: false,
            cancellationToken: stoppingToken);

        AsyncEventingBasicConsumer consumer = new(consumerChannel);
        consumer.ReceivedAsync += async (_, deliveryArguments) =>
        {
            try
            {
                await HandleDeliveryAsync(consumerChannel, deliveryArguments, stoppingToken);
            }
            catch (Exception deliveryHandlingException)
            {
                // The broker callback has nobody to propagate to: an escaped exception here would
                // become an unobserved task. The delivery stays unacknowledged and is redelivered.
                logger.LogError(
                    deliveryHandlingException,
                    "Handling delivery {DeliveryTag} failed outside the processing pipeline; it will be redelivered.",
                    deliveryArguments.DeliveryTag);
            }
        };

        string consumerTag = await consumerChannel.BasicConsumeAsync(
            queue: IncomingMessageQueueTopology.QueueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        logger.LogInformation(
            "Consuming {QueueName} with prefetch {PrefetchCount} (consumer tag {ConsumerTag}).",
            IncomingMessageQueueTopology.QueueName,
            options.PrefetchCount,
            consumerTag);

        // The consumer runs on broker callbacks; this loop only keeps the channel alive and notices
        // when it closes, so the outer loop can rebuild connection, channel and subscription.
        while (!stoppingToken.IsCancellationRequested && consumerChannel.IsOpen)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }

        logger.LogWarning("The RabbitMQ consumer channel is no longer open; the subscription will be rebuilt.");
    }

    private async Task HandleDeliveryAsync(
        IChannel consumerChannel,
        BasicDeliverEventArgs deliveryArguments,
        CancellationToken stoppingToken)
    {
        IncomingTelegramMessage? incomingTelegramMessage;
        try
        {
            incomingTelegramMessage = JsonSerializer.Deserialize<IncomingTelegramMessage>(
                deliveryArguments.Body.Span,
                IncomingMessageQueueSerialization.Options);
        }
        catch (JsonException deserialisationException)
        {
            logger.LogError(
                deserialisationException,
                "Discarding an unreadable delivery (message id {MessageId}) to the dead-letter queue.",
                deliveryArguments.BasicProperties.MessageId);

            await DeadLetterAsync(consumerChannel, deliveryArguments);
            return;
        }

        if (incomingTelegramMessage is null)
        {
            logger.LogError(
                "Discarding an empty delivery (message id {MessageId}) to the dead-letter queue.",
                deliveryArguments.BasicProperties.MessageId);

            await DeadLetterAsync(consumerChannel, deliveryArguments);
            return;
        }

        DeliveryDisposition disposition =
            await TryProcessWithBoundedRetriesAsync(incomingTelegramMessage, stoppingToken);

        switch (disposition)
        {
            case DeliveryDisposition.Acknowledge:
                await consumerChannel.BasicAckAsync(
                    deliveryArguments.DeliveryTag,
                    multiple: false,
                    cancellationToken: CancellationToken.None);
                break;

            case DeliveryDisposition.DeadLetter:
                await DeadLetterAsync(consumerChannel, deliveryArguments);
                break;

            case DeliveryDisposition.LeaveUnacknowledged:
                // The host is shutting down, not rejecting the message. Returning without acking or
                // nacking leaves the delivery unconfirmed, so RabbitMQ requeues it when the channel
                // closes and the next consumer picks it up. Nacking here would dead-letter a message
                // whose only sin was arriving during a restart.
                break;
        }
    }

    /// <summary>
    /// What should happen to a delivery once processing stops. Rejecting and simply not confirming
    /// are deliberately distinct: only the first is a verdict about the message itself.
    /// </summary>
    private enum DeliveryDisposition
    {
        Acknowledge,
        DeadLetter,
        LeaveUnacknowledged
    }

    private async Task<DeliveryDisposition> TryProcessWithBoundedRetriesAsync(
        IncomingTelegramMessage incomingTelegramMessage,
        CancellationToken stoppingToken)
    {
        for (int attemptNumber = 1; attemptNumber <= options.MaximumProcessingAttempts; attemptNumber++)
        {
            try
            {
                await using AsyncServiceScope serviceScope = serviceScopeFactory.CreateAsyncScope();
                ProcessIncomingTelegramMessageHandler handler = serviceScope.ServiceProvider
                    .GetRequiredService<ProcessIncomingTelegramMessageHandler>();

                IncomingMessageProcessingOutcome outcome =
                    await handler.HandleAsync(incomingTelegramMessage, stoppingToken);

                if (outcome == IncomingMessageProcessingOutcome.DuplicateDiscarded)
                {
                    logger.LogDebug(
                        "Telegram update {UpdateId} was already stored; acknowledging the redelivery.",
                        incomingTelegramMessage.UpdateId);
                }

                return DeliveryDisposition.Acknowledge;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                logger.LogInformation(
                    "Shutting down while processing Telegram update {UpdateId}; leaving the delivery "
                        + "unconfirmed so RabbitMQ redelivers it.",
                    incomingTelegramMessage.UpdateId);

                return DeliveryDisposition.LeaveUnacknowledged;
            }
            catch (DomainRuleViolationException domainRuleViolationException)
            {
                logger.LogError(
                    domainRuleViolationException,
                    "Telegram update {UpdateId} violates a domain rule and can never be stored; dead-lettering it.",
                    incomingTelegramMessage.UpdateId);

                return DeliveryDisposition.DeadLetter;
            }
            catch (Exception processingException)
            {
                bool hasAttemptsLeft = attemptNumber < options.MaximumProcessingAttempts;
                logger.LogError(
                    processingException,
                    "Attempt {AttemptNumber}/{MaximumProcessingAttempts} to process Telegram update {UpdateId} failed.",
                    attemptNumber,
                    options.MaximumProcessingAttempts,
                    incomingTelegramMessage.UpdateId);

                if (!hasAttemptsLeft)
                {
                    return DeliveryDisposition.DeadLetter;
                }

                await Task.Delay(options.ProcessingRetryDelay * attemptNumber, stoppingToken);
            }
        }

        return DeliveryDisposition.DeadLetter;
    }

    private async Task DeadLetterAsync(IChannel consumerChannel, BasicDeliverEventArgs deliveryArguments)
    {
        // requeue: false routes the delivery to the configured dead-letter exchange instead of
        // putting it back at the head of the queue, which would loop forever on a poison message.
        await consumerChannel.BasicNackAsync(
            deliveryArguments.DeliveryTag,
            multiple: false,
            requeue: false,
            cancellationToken: CancellationToken.None);

        logger.LogWarning(
            "Delivery {DeliveryTag} was routed to {DeadLetterQueueName}.",
            deliveryArguments.DeliveryTag,
            IncomingMessageQueueTopology.DeadLetterQueueName);
    }
}

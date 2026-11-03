using EncodeLabs.Application.Abstractions.Messaging;
using EncodeLabs.Domain.Common;
using Microsoft.Extensions.Logging;

namespace EncodeLabs.Infrastructure.Messaging.RabbitMq;

/// <summary>
/// Decides what to do with one delivery: how many times to retry processing it, and whether the
/// outcome means confirm, dead-letter, or leave it unconfirmed.
/// </summary>
/// <remarks>
/// This lives apart from <see cref="IncomingTelegramMessageConsumerService"/> on purpose. The
/// consumer owns the RabbitMQ plumbing —channels, delivery tags, acknowledgements— while this owns
/// the decision, which is the part that has already been wrong once (CR-018) and the part worth
/// testing without a broker.
/// </remarks>
public sealed class IncomingMessageRetryPolicy(
    int maximumProcessingAttempts,
    TimeSpan processingRetryDelay,
    ILogger logger)
{
    /// <summary>
    /// Runs <paramref name="processMessageAsync"/> until it succeeds or the attempts run out.
    /// </summary>
    /// <param name="delayBetweenAttemptsAsync">
    /// How to wait between attempts. Injected so tests do not spend real seconds sleeping.
    /// </param>
    public async Task<DeliveryDisposition> ProcessWithBoundedRetriesAsync(
        IncomingTelegramMessage incomingTelegramMessage,
        Func<IncomingTelegramMessage, CancellationToken, Task> processMessageAsync,
        Func<TimeSpan, CancellationToken, Task> delayBetweenAttemptsAsync,
        CancellationToken stoppingToken)
    {
        for (int attemptNumber = 1; attemptNumber <= maximumProcessingAttempts; attemptNumber++)
        {
            try
            {
                await processMessageAsync(incomingTelegramMessage, stoppingToken);
                return DeliveryDisposition.Acknowledge;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutting down is not a verdict about the message. Dead-lettering it here would
                // discard a perfectly good message whose only misfortune was arriving mid-restart,
                // and Telegram will not send it again because the webhook already answered 202.
                logger.LogInformation(
                    "Shutting down while processing Telegram update {UpdateId}; leaving the delivery "
                        + "unconfirmed so RabbitMQ redelivers it.",
                    incomingTelegramMessage.UpdateId);

                return DeliveryDisposition.LeaveUnacknowledged;
            }
            catch (DomainRuleViolationException domainRuleViolationException)
            {
                // Retrying cannot help: the message violates a rule it will still violate next time.
                logger.LogError(
                    domainRuleViolationException,
                    "Telegram update {UpdateId} violates a domain rule and can never be stored; dead-lettering it.",
                    incomingTelegramMessage.UpdateId);

                return DeliveryDisposition.DeadLetter;
            }
            catch (Exception processingException)
            {
                logger.LogError(
                    processingException,
                    "Attempt {AttemptNumber}/{MaximumProcessingAttempts} to process Telegram update {UpdateId} failed.",
                    attemptNumber,
                    maximumProcessingAttempts,
                    incomingTelegramMessage.UpdateId);

                if (attemptNumber >= maximumProcessingAttempts)
                {
                    return DeliveryDisposition.DeadLetter;
                }

                await delayBetweenAttemptsAsync(processingRetryDelay * attemptNumber, stoppingToken);
            }
        }

        return DeliveryDisposition.DeadLetter;
    }
}

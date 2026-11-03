using EncodeLabs.Application.Abstractions.Messaging;
using EncodeLabs.Domain.Common;
using EncodeLabs.Infrastructure.Messaging.RabbitMq;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EncodeLabs.Infrastructure.UnitTests.Messaging;

/// <summary>
/// Regression tests for CR-018: a delivery that fails must be told apart from a delivery that was
/// merely interrupted.
/// </summary>
/// <remarks>
/// The defect this guards against cost a real message: the shutdown path returned the same value as
/// a poison message, so restarting the API dead-lettered whatever it was processing. These tests
/// never touch RabbitMQ; they pin the decision, which is where the mistake was.
/// </remarks>
public sealed class IncomingMessageRetryPolicyTests
{
    private const int MaximumAttempts = 3;

    private static readonly IncomingTelegramMessage AnyMessage = new(
        UpdateId: 42,
        TelegramMessageId: 7,
        TelegramUserId: 101,
        TelegramChatId: 101,
        FirstName: "Ana",
        LastName: null,
        Username: null,
        Text: "hola",
        SentAtUtc: new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero));

    private static IncomingMessageRetryPolicy CreatePolicy() =>
        new(MaximumAttempts, TimeSpan.FromMilliseconds(1), NullLogger.Instance);

    /// <summary>Waiting is replaced so the suite does not spend real seconds asleep.</summary>
    private static Task NoDelay(TimeSpan _, CancellationToken __) => Task.CompletedTask;

    [Fact]
    public async Task AMessageThatProcessesCleanlyIsAcknowledged()
    {
        int attemptsMade = 0;

        DeliveryDisposition disposition = await CreatePolicy().ProcessWithBoundedRetriesAsync(
            AnyMessage,
            (_, _) => { attemptsMade++; return Task.CompletedTask; },
            NoDelay,
            CancellationToken.None);

        Assert.Equal(DeliveryDisposition.Acknowledge, disposition);
        Assert.Equal(1, attemptsMade);
    }

    /// <summary>
    /// The regression itself. Shutting down is not a verdict about the message: dead-lettering it
    /// would discard a good message that Telegram will never send again, because the webhook already
    /// answered `202`.
    /// </summary>
    [Fact]
    public async Task ShutdownLeavesTheDeliveryUnacknowledgedInsteadOfDeadLetteringIt()
    {
        using CancellationTokenSource shutdownSource = new();
        await shutdownSource.CancelAsync();

        DeliveryDisposition disposition = await CreatePolicy().ProcessWithBoundedRetriesAsync(
            AnyMessage,
            (_, cancellationToken) => Task.FromCanceled(cancellationToken),
            NoDelay,
            shutdownSource.Token);

        Assert.Equal(DeliveryDisposition.LeaveUnacknowledged, disposition);
        Assert.NotEqual(DeliveryDisposition.DeadLetter, disposition);
    }

    /// <summary>A cancellation that is not our shutdown is an ordinary failure, and gets retried.</summary>
    [Fact]
    public async Task ACancellationUnrelatedToShutdownIsTreatedAsAFailure()
    {
        int attemptsMade = 0;

        DeliveryDisposition disposition = await CreatePolicy().ProcessWithBoundedRetriesAsync(
            AnyMessage,
            (_, _) =>
            {
                attemptsMade++;
                return Task.FromException(new OperationCanceledException("an unrelated timeout"));
            },
            NoDelay,
            CancellationToken.None);

        Assert.Equal(DeliveryDisposition.DeadLetter, disposition);
        Assert.Equal(MaximumAttempts, attemptsMade);
    }

    [Fact]
    public async Task ATransientFailureIsRetriedAndAcknowledgedWhenItRecovers()
    {
        int attemptsMade = 0;

        DeliveryDisposition disposition = await CreatePolicy().ProcessWithBoundedRetriesAsync(
            AnyMessage,
            (_, _) =>
            {
                attemptsMade++;
                return attemptsMade < MaximumAttempts
                    ? Task.FromException(new InvalidOperationException("the database is down"))
                    : Task.CompletedTask;
            },
            NoDelay,
            CancellationToken.None);

        Assert.Equal(DeliveryDisposition.Acknowledge, disposition);
        Assert.Equal(MaximumAttempts, attemptsMade);
    }

    [Fact]
    public async Task AFailureThatNeverRecoversIsDeadLetteredAfterExactlyTheAllowedAttempts()
    {
        int attemptsMade = 0;

        DeliveryDisposition disposition = await CreatePolicy().ProcessWithBoundedRetriesAsync(
            AnyMessage,
            (_, _) =>
            {
                attemptsMade++;
                return Task.FromException(new InvalidOperationException("the database is down"));
            },
            NoDelay,
            CancellationToken.None);

        Assert.Equal(DeliveryDisposition.DeadLetter, disposition);
        Assert.Equal(MaximumAttempts, attemptsMade);
    }

    /// <summary>A message that breaks a domain rule will break it again: retrying wastes time.</summary>
    [Fact]
    public async Task ADomainRuleViolationIsDeadLetteredWithoutRetrying()
    {
        int attemptsMade = 0;

        DeliveryDisposition disposition = await CreatePolicy().ProcessWithBoundedRetriesAsync(
            AnyMessage,
            (_, _) =>
            {
                attemptsMade++;
                return Task.FromException(new DomainRuleViolationException("the text is empty"));
            },
            NoDelay,
            CancellationToken.None);

        Assert.Equal(DeliveryDisposition.DeadLetter, disposition);
        Assert.Equal(1, attemptsMade);
    }

    /// <summary>The back-off grows with each attempt rather than hammering a service that is down.</summary>
    [Fact]
    public async Task TheWaitBetweenAttemptsGrowsWithEachAttempt()
    {
        List<TimeSpan> waits = [];

        await CreatePolicy().ProcessWithBoundedRetriesAsync(
            AnyMessage,
            (_, _) => Task.FromException(new InvalidOperationException("the database is down")),
            (waitDuration, _) => { waits.Add(waitDuration); return Task.CompletedTask; },
            CancellationToken.None);

        Assert.Equal(MaximumAttempts - 1, waits.Count);
        Assert.True(waits[1] > waits[0], "the second wait should be longer than the first");
    }
}

using EncodeLabs.Api.ErrorHandling;
using EncodeLabs.Application.Abstractions.Messaging;
using EncodeLabs.Application.Abstractions.Telegram;
using EncodeLabs.Application.Inbox;
using EncodeLabs.Domain.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EncodeLabs.Api.UnitTests.ErrorHandling;

/// <summary>
/// Pins the translation from failure to HTTP response.
/// </summary>
/// <remarks>
/// The status code is a contract, not a formality: the client words its message from it, and
/// Telegram's retry machinery reads it. Two defects came from this mapping —a rejection and an
/// unreachable Bot API shared a code (CR-016), and a malformed body surfaced as a server fault
/// (CR-021)— so every case is pinned here.
/// </remarks>
public sealed class InboxExceptionHandlerTests
{
    /// <summary>Captures what the handler wrote instead of serialising it to a response body.</summary>
    private sealed class CapturingProblemDetailsService : IProblemDetailsService
    {
        internal ProblemDetails? Written { get; private set; }

        public ValueTask WriteAsync(ProblemDetailsContext context)
        {
            Written = context.ProblemDetails;
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> TryWriteAsync(ProblemDetailsContext context)
        {
            Written = context.ProblemDetails;
            return ValueTask.FromResult(true);
        }
    }

    private static async Task<(int StatusCode, ProblemDetails Problem)> HandleAsync(Exception exception)
    {
        CapturingProblemDetailsService problemDetailsService = new();
        InboxExceptionHandler handler = new(problemDetailsService, NullLogger<InboxExceptionHandler>.Instance);
        DefaultHttpContext httpContext = new();

        bool handled = await handler.TryHandleAsync(httpContext, exception, CancellationToken.None);

        Assert.True(handled, "the handler should take responsibility for every inbox failure");
        Assert.NotNull(problemDetailsService.Written);

        return (httpContext.Response.StatusCode, problemDetailsService.Written);
    }

    [Fact]
    public async Task AnUnknownContactIsNotFound()
    {
        (int statusCode, _) = await HandleAsync(new ContactNotFoundException(Guid.NewGuid()));

        Assert.Equal(StatusCodes.Status404NotFound, statusCode);
    }

    [Fact]
    public async Task ABrokenDomainRuleIsABadRequest()
    {
        (int statusCode, _) = await HandleAsync(new DomainRuleViolationException("the text is empty"));

        Assert.Equal(StatusCodes.Status400BadRequest, statusCode);
    }

    /// <summary>
    /// CR-021. A body the model binder cannot read is the caller's fault. It used to fall through to
    /// `500`, which also logged it at error level — and on a public webhook that means anyone can
    /// fill the log with noise by posting garbage.
    /// </summary>
    [Fact]
    public async Task AnUnreadableRequestBodyIsABadRequestAndNotAServerFault()
    {
        (int statusCode, _) = await HandleAsync(new BadHttpRequestException("the body could not be read"));

        Assert.Equal(StatusCodes.Status400BadRequest, statusCode);
        Assert.True(statusCode < StatusCodes.Status500InternalServerError);
    }

    /// <summary>
    /// CR-016. Telegram refusing and Telegram being unreachable demand opposite reactions, so they
    /// must not share a status code: retrying helps with one and never with the other.
    /// </summary>
    [Fact]
    public async Task ARejectionByTelegramAndAnUnreachableBotApiGetDifferentStatusCodes()
    {
        (int rejectionStatus, ProblemDetails rejectionProblem) = await HandleAsync(
            new TelegramReplyFailedException(
                "Telegram rejected the reply: Forbidden: bot was blocked by the user",
                "Forbidden: bot was blocked by the user"));

        (int unreachableStatus, _) = await HandleAsync(
            new TelegramReplyFailedException("The Telegram Bot API could not be reached."));

        Assert.Equal(StatusCodes.Status502BadGateway, rejectionStatus);
        Assert.Equal(StatusCodes.Status504GatewayTimeout, unreachableStatus);
        Assert.NotEqual(rejectionStatus, unreachableStatus);

        // Telegram's own words are the actionable part and must survive to the client.
        Assert.Equal("Forbidden: bot was blocked by the user", rejectionProblem.Detail);
    }

    [Fact]
    public async Task AnUnavailableBrokerAsksTheCallerToRetry()
    {
        (int statusCode, _) = await HandleAsync(new MessageQueuePublishFailedException("no confirmation"));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, statusCode);
    }

    /// <summary>
    /// CR-020. This one shares its status code with every other server fault, so it carries a stable
    /// `type` instead: the client matches on that to tell the operator **not** to send it again.
    /// </summary>
    [Fact]
    public async Task AReplyDeliveredButNotRecordedIsMarkedWithAStableProblemType()
    {
        (int statusCode, ProblemDetails problem) = await HandleAsync(
            new ReplyDeliveredButNotRecordedException(99, new InvalidOperationException("the database is down")));

        Assert.Equal(StatusCodes.Status500InternalServerError, statusCode);
        Assert.Equal(InboxExceptionHandler.ReplyDeliveredButNotRecordedProblemType, problem.Type);
    }

    /// <summary>Only that one failure carries the marker; anything else must not be mistaken for it.</summary>
    [Fact]
    public async Task NoOtherFailureCarriesThatProblemType()
    {
        foreach (Exception exception in new Exception[]
                 {
                     new ContactNotFoundException(Guid.NewGuid()),
                     new DomainRuleViolationException("the text is empty"),
                     new TelegramReplyFailedException("rejected", "Forbidden"),
                     new MessageQueuePublishFailedException("no confirmation"),
                     new InvalidOperationException("something else")
                 })
        {
            (_, ProblemDetails problem) = await HandleAsync(exception);

            Assert.NotEqual(InboxExceptionHandler.ReplyDeliveredButNotRecordedProblemType, problem.Type);
        }
    }

    [Fact]
    public async Task AnUnexpectedFailureNeverLeaksItsMessageToTheCaller()
    {
        const string secretishDetail = "Host=postgres;Password=hunter2";

        (int statusCode, ProblemDetails problem) = await HandleAsync(new InvalidOperationException(secretishDetail));

        Assert.Equal(StatusCodes.Status500InternalServerError, statusCode);
        Assert.DoesNotContain(secretishDetail, problem.Detail ?? string.Empty, StringComparison.Ordinal);
    }
}

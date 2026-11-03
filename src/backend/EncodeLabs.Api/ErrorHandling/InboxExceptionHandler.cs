using EncodeLabs.Application.Abstractions.Messaging;
using EncodeLabs.Application.Abstractions.Telegram;
using EncodeLabs.Application.Inbox;
using EncodeLabs.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace EncodeLabs.Api.ErrorHandling;

/// <summary>
/// Translates the application's failure modes into problem responses the Angular client can act on.
/// A failed Telegram delivery in particular must never look like a success.
/// </summary>
/// <remarks>
/// <c>Detail</c> carries the technical reason, not the sentence the operator reads: the client owns
/// the interface language and composes its own wording from the status code.
/// </remarks>
public sealed class InboxExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<InboxExceptionHandler> logger) : IExceptionHandler
{
    /// <summary>
    /// Marks the response for a reply Telegram delivered but the inbox failed to store. The client
    /// matches on this rather than on the status code, which it shares with unrelated failures.
    /// </summary>
    public const string ReplyDeliveredButNotRecordedProblemType =
        "https://encodelabs.inbox/problems/reply-delivered-but-not-recorded";


    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        (int statusCode, string title, string detail) = DescribeFailure(exception);

        // A stable identifier for the one failure the client must treat specially. Status alone is
        // not enough: this is a 500 like any other, but the operator must not retry it.
        string? problemType = exception is ReplyDeliveredButNotRecordedException
            ? ReplyDeliveredButNotRecordedProblemType
            : null;

        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Request {RequestPath} failed with status {StatusCode}.", httpContext.Request.Path, statusCode);
        }
        else
        {
            logger.LogInformation("Request {RequestPath} rejected with status {StatusCode}: {Detail}", httpContext.Request.Path, statusCode, detail);
        }

        httpContext.Response.StatusCode = statusCode;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = statusCode,
                Title = title,
                Detail = detail,
                Type = problemType
            }
        });
    }

    private static (int StatusCode, string Title, string Detail) DescribeFailure(Exception exception) => exception switch
    {
        ContactNotFoundException contactNotFoundException => (
            StatusCodes.Status404NotFound,
            "Contact not found",
            contactNotFoundException.Message),

        DomainRuleViolationException domainRuleViolationException => (
            StatusCodes.Status400BadRequest,
            "Invalid request",
            domainRuleViolationException.Message),

        // A body the model binder could not read is a malformed request, not a server fault. Without
        // this case it surfaced as 500 and was logged at Error level, which on a public webhook means
        // anyone can fill the log with noise by posting garbage.
        BadHttpRequestException => (
            StatusCodes.Status400BadRequest,
            "Invalid request",
            "The request body could not be read."),

        // Telegram already delivered this reply, so the operator must not send it again. It is a 500
        // because the failure is ours, but the wording exists to stop a retry that would duplicate
        // the message on the contact's phone.
        ReplyDeliveredButNotRecordedException => (
            StatusCodes.Status500InternalServerError,
            "The reply was delivered but not recorded",
            "The reply reached Telegram but could not be stored. Do not send it again; refresh the inbox."),

        // A rejection and an unreachable API are told apart by status so the client can word them
        // differently: the first will fail again on retry, the second may not.
        TelegramReplyFailedException { TelegramDescription: { } telegramDescription } => (
            StatusCodes.Status502BadGateway,
            "The reply was not delivered",
            telegramDescription),

        TelegramReplyFailedException => (
            StatusCodes.Status504GatewayTimeout,
            "The reply was not delivered",
            "The Telegram Bot API could not be reached."),

        MessageQueuePublishFailedException => (
            StatusCodes.Status503ServiceUnavailable,
            "The message broker is unavailable",
            "The message could not be queued for processing. Please retry."),

        _ => (
            StatusCodes.Status500InternalServerError,
            "Unexpected error",
            "The request could not be completed. See the API logs for details.")
    };
}

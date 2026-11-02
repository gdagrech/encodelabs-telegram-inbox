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
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        (int statusCode, string title, string detail) = DescribeFailure(exception);

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
                Detail = detail
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

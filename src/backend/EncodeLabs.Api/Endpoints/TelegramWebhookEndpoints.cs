using System.Security.Cryptography;
using System.Text;
using EncodeLabs.Api.Contracts;
using EncodeLabs.Application.Abstractions.Messaging;
using EncodeLabs.Application.Abstractions.Telegram;
using EncodeLabs.Infrastructure.Telegram;
using Microsoft.Extensions.Options;

namespace EncodeLabs.Api.Endpoints;

/// <summary>
/// The publicly reachable Telegram webhook. It does as little as possible: authenticate, validate,
/// enqueue, answer. All persistence happens in the queue consumer so Telegram's delivery timeout is
/// never spent waiting for the database.
/// </summary>
public static class TelegramWebhookEndpoints
{
    public const string SecretTokenHeaderName = "X-Telegram-Bot-Api-Secret-Token";

    public static IEndpointRouteBuilder MapTelegramWebhookEndpoints(
        this IEndpointRouteBuilder endpointRouteBuilder,
        string webhookPath)
    {
        endpointRouteBuilder
            .MapPost(webhookPath, ReceiveTelegramUpdateAsync)
            .WithName("ReceiveTelegramUpdate")
            // Keeps the public webhook path out of the generated API description.
            .ExcludeFromDescription();

        endpointRouteBuilder
            .MapGet("/api/telegram/webhook-status", GetWebhookStatusAsync)
            .WithName("GetTelegramWebhookStatus")
            .WithSummary("Reports the webhook Telegram currently has on record, without exposing any secret.");

        return endpointRouteBuilder;
    }

    private static async Task<IResult> ReceiveTelegramUpdateAsync(
        TelegramWebhookPayload webhookPayload,
        HttpRequest httpRequest,
        IOptions<TelegramBotOptions> telegramBotOptions,
        IIncomingMessageQueuePublisher incomingMessageQueuePublisher,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        ILogger logger = loggerFactory.CreateLogger(typeof(TelegramWebhookEndpoints));

        if (!HasValidSecretToken(httpRequest, telegramBotOptions.Value.WebhookSecretToken))
        {
            logger.LogWarning(
                "Rejected a webhook call with a missing or incorrect secret token header from {RemoteIpAddress}.",
                httpRequest.HttpContext.Connection.RemoteIpAddress);

            return Results.Unauthorized();
        }

        if (!TryAcceptTextMessage(webhookPayload, out IncomingTelegramMessage? incomingTelegramMessage, out string rejectionReason))
        {
            // Answering 200 tells Telegram the update was handled so it stops redelivering something
            // this inbox will never process. Media, edits and service messages are out of scope.
            logger.LogInformation(
                "Ignoring Telegram update {UpdateId}: {RejectionReason}.",
                webhookPayload.UpdateId,
                rejectionReason);

            return Results.Ok();
        }

        try
        {
            await incomingMessageQueuePublisher.PublishConfirmedAsync(incomingTelegramMessage!, cancellationToken);
        }
        catch (MessageQueuePublishFailedException publishFailedException)
        {
            // Never acknowledge a message that is not safely queued: a non-2xx answer makes Telegram retry.
            logger.LogError(
                publishFailedException,
                "Could not queue Telegram update {UpdateId}; answering 503 so Telegram retries the delivery.",
                webhookPayload.UpdateId);

            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Accepted();
    }

    private static async Task<IResult> GetWebhookStatusAsync(
        ITelegramWebhookRegistrar telegramWebhookRegistrar,
        CancellationToken cancellationToken)
    {
        TelegramWebhookStatus webhookStatus =
            await telegramWebhookRegistrar.GetWebhookStatusAsync(cancellationToken);

        return Results.Ok(webhookStatus);
    }

    private static bool HasValidSecretToken(HttpRequest httpRequest, string expectedSecretToken)
    {
        if (!httpRequest.Headers.TryGetValue(SecretTokenHeaderName, out var providedSecretTokenValues))
        {
            return false;
        }

        string? providedSecretToken = providedSecretTokenValues.ToString();
        if (string.IsNullOrEmpty(providedSecretToken))
        {
            return false;
        }

        // Fixed-time comparison so the response time cannot be used to recover the token byte by byte.
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(providedSecretToken),
            Encoding.UTF8.GetBytes(expectedSecretToken));
    }

    /// <summary>
    /// Accepts only what this inbox supports: a text message sent by a human in a one-to-one chat.
    /// </summary>
    private static bool TryAcceptTextMessage(
        TelegramWebhookPayload webhookPayload,
        out IncomingTelegramMessage? incomingTelegramMessage,
        out string rejectionReason)
    {
        incomingTelegramMessage = null;

        if (webhookPayload.Message is null)
        {
            rejectionReason = "the update carries no message (edits, callbacks and service updates are out of scope)";
            return false;
        }

        TelegramWebhookMessage telegramMessage = webhookPayload.Message;

        if (string.IsNullOrWhiteSpace(telegramMessage.Text))
        {
            rejectionReason = "the message has no text content (media messages are out of scope)";
            return false;
        }

        if (telegramMessage.Sender is null)
        {
            rejectionReason = "the message has no sender";
            return false;
        }

        if (telegramMessage.Sender.IsBot)
        {
            rejectionReason = "the message was sent by a bot";
            return false;
        }

        if (telegramMessage.Chat is null
            || !string.Equals(
                telegramMessage.Chat.Type,
                TelegramWebhookChat.PrivateChatType,
                StringComparison.OrdinalIgnoreCase))
        {
            rejectionReason = "the message does not belong to a private chat (groups and channels are out of scope)";
            return false;
        }

        incomingTelegramMessage = new IncomingTelegramMessage(
            webhookPayload.UpdateId,
            telegramMessage.MessageId,
            telegramMessage.Sender.Id,
            telegramMessage.Chat.Id,
            telegramMessage.Sender.FirstName,
            telegramMessage.Sender.LastName,
            telegramMessage.Sender.Username,
            telegramMessage.Text,
            DateTimeOffset.FromUnixTimeSeconds(telegramMessage.SentAtUnixSeconds));

        rejectionReason = string.Empty;
        return true;
    }
}

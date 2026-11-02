using EncodeLabs.Application.Abstractions.Telegram;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types.Enums;

// Aliased because the Telegram SDK and this solution's domain both define a type called Message.
using TelegramApiMessage = Telegram.Bot.Types.Message;
using TelegramApiWebhookInfo = Telegram.Bot.Types.WebhookInfo;

namespace EncodeLabs.Infrastructure.Telegram;

/// <summary>
/// Thin adapter over the Telegram Bot API covering the two things this system needs: delivering
/// operator replies and keeping the webhook registration current.
/// </summary>
public sealed class TelegramBotApiClient(
    ITelegramBotClient telegramBotClient,
    IOptions<TelegramBotOptions> options,
    ILogger<TelegramBotApiClient> logger) : ITelegramReplySender, ITelegramWebhookRegistrar
{
    private readonly TelegramBotOptions options = options.Value;

    public async Task<TelegramReplyDelivery> SendTextMessageAsync(
        long telegramChatId,
        string text,
        CancellationToken cancellationToken)
    {
        try
        {
            TelegramApiMessage deliveredMessage = await telegramBotClient.SendMessage(
                chatId: telegramChatId,
                text: text,
                cancellationToken: cancellationToken);

            return new TelegramReplyDelivery(
                deliveredMessage.MessageId,
                ToUtcTimestamp(deliveredMessage.Date));
        }
        catch (ApiRequestException apiRequestException)
        {
            // Telegram's own description is safe to surface: it never contains the bot token. It is
            // carried separately from the log message so the client can word the operator-facing
            // sentence itself instead of echoing English prose into a Spanish interface.
            throw new TelegramReplyFailedException(
                $"Telegram rejected the reply: {apiRequestException.Message}",
                apiRequestException.Message,
                apiRequestException);
        }
        catch (Exception transportException) when (transportException is HttpRequestException or TaskCanceledException)
        {
            throw new TelegramReplyFailedException(
                "The Telegram Bot API could not be reached. The reply was not delivered.",
                telegramDescription: null,
                transportException);
        }
    }

    public async Task RegisterWebhookAsync(Uri publicWebhookUrl, CancellationToken cancellationToken)
    {
        try
        {
            await telegramBotClient.SetWebhook(
                url: publicWebhookUrl.ToString(),
                secretToken: options.WebhookSecretToken,
                allowedUpdates: [UpdateType.Message],
                dropPendingUpdates: false,
                cancellationToken: cancellationToken);

            logger.LogInformation("Telegram webhook registered for {PublicWebhookUrl}.", publicWebhookUrl);
        }
        catch (Exception registrationException)
            when (registrationException is ApiRequestException or HttpRequestException or TaskCanceledException)
        {
            throw new TelegramWebhookRegistrationFailedException(
                $"Telegram did not accept the webhook registration for {publicWebhookUrl}: {registrationException.Message}",
                registrationException);
        }
    }

    public async Task<TelegramWebhookStatus> GetWebhookStatusAsync(CancellationToken cancellationToken)
    {
        TelegramApiWebhookInfo webhookInfo = await telegramBotClient.GetWebhookInfo(cancellationToken);

        return new TelegramWebhookStatus(
            webhookInfo.Url,
            webhookInfo.PendingUpdateCount,
            webhookInfo.LastErrorMessage,
            webhookInfo.LastErrorDate is null ? null : ToUtcTimestamp(webhookInfo.LastErrorDate.Value));
    }

    private static DateTimeOffset ToUtcTimestamp(DateTime telegramTimestamp) =>
        new(DateTime.SpecifyKind(telegramTimestamp, DateTimeKind.Utc));
}

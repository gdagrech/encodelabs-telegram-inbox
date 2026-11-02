using EncodeLabs.Application.Abstractions.Telegram;
using EncodeLabs.Application.Abstractions.Tunnelling;
using EncodeLabs.Infrastructure.Telegram;
using Microsoft.Extensions.Options;

namespace EncodeLabs.Api.BackgroundServices;

/// <summary>
/// Registers this instance's webhook with Telegram on every start-up, using whatever public address
/// ngrok handed out this time. This is what makes <c>docker compose up</c> sufficient: the tunnel URL
/// changes on each restart and is therefore discovered, never configured.
/// </summary>
public sealed class TelegramWebhookRegistrationService(
    IPublicTunnelUrlProvider publicTunnelUrlProvider,
    ITelegramWebhookRegistrar telegramWebhookRegistrar,
    IOptions<TelegramBotOptions> options,
    IHostApplicationLifetime hostApplicationLifetime,
    ILogger<TelegramWebhookRegistrationService> logger) : BackgroundService
{
    private readonly TelegramBotOptions options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Registering before Kestrel accepts requests would point Telegram at an endpoint that is not
        // serving yet, so the first deliveries would fail and be retried needlessly.
        await WaitUntilApplicationHasStartedAsync(stoppingToken);

        for (int attemptNumber = 1; attemptNumber <= options.WebhookRegistrationMaximumAttempts; attemptNumber++)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            if (await TryRegisterWebhookAsync(attemptNumber, stoppingToken))
            {
                return;
            }

            await Task.Delay(options.WebhookRegistrationRetryDelay, stoppingToken);
        }

        logger.LogCritical(
            "Giving up after {MaximumAttempts} attempts to register the Telegram webhook. Incoming messages will not arrive. "
            + "Check that NGROK_AUTHTOKEN and TELEGRAM_BOT_TOKEN in .env are valid, that the ngrok container is running, "
            + "and then restart the stack.",
            options.WebhookRegistrationMaximumAttempts);
    }

    private async Task<bool> TryRegisterWebhookAsync(int attemptNumber, CancellationToken cancellationToken)
    {
        Uri? publicBaseAddress = await publicTunnelUrlProvider.TryGetPublicHttpsBaseAddressAsync(cancellationToken);
        if (publicBaseAddress is null)
        {
            logger.LogInformation(
                "Attempt {AttemptNumber}/{MaximumAttempts}: no public HTTPS tunnel available yet; retrying in {RetryDelaySeconds}s.",
                attemptNumber,
                options.WebhookRegistrationMaximumAttempts,
                options.WebhookRegistrationRetryDelay.TotalSeconds);

            return false;
        }

        Uri publicWebhookUrl = new(publicBaseAddress, options.WebhookPath);

        try
        {
            await telegramWebhookRegistrar.RegisterWebhookAsync(publicWebhookUrl, cancellationToken);
        }
        catch (TelegramWebhookRegistrationFailedException registrationFailedException)
        {
            logger.LogWarning(
                registrationFailedException,
                "Attempt {AttemptNumber}/{MaximumAttempts} to register the Telegram webhook failed; retrying in {RetryDelaySeconds}s.",
                attemptNumber,
                options.WebhookRegistrationMaximumAttempts,
                options.WebhookRegistrationRetryDelay.TotalSeconds);

            return false;
        }

        await LogWebhookStatusAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Reads the registration back from Telegram so the log shows the state Telegram actually holds
    /// rather than the state this process believes it set. The secret token is never part of it.
    /// </summary>
    private async Task LogWebhookStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            TelegramWebhookStatus webhookStatus =
                await telegramWebhookRegistrar.GetWebhookStatusAsync(cancellationToken);

            logger.LogInformation(
                "Telegram reports webhook {RegisteredUrl} with {PendingUpdateCount} pending update(s). Last error: {LastErrorMessage}.",
                webhookStatus.RegisteredUrl,
                webhookStatus.PendingUpdateCount,
                webhookStatus.LastErrorMessage ?? "none");
        }
        catch (Exception webhookStatusException)
        {
            logger.LogWarning(
                webhookStatusException,
                "The webhook was registered but reading its status back from Telegram failed.");
        }
    }

    private async Task WaitUntilApplicationHasStartedAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource applicationStartedSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);

        await using CancellationTokenRegistration startedRegistration = hostApplicationLifetime.ApplicationStarted
            .Register(() => applicationStartedSignal.TrySetResult());

        await applicationStartedSignal.Task.WaitAsync(cancellationToken);
    }
}

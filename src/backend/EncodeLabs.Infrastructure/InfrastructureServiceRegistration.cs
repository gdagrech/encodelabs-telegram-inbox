using EncodeLabs.Application.Abstractions.Messaging;
using EncodeLabs.Application.Abstractions.Persistence;
using EncodeLabs.Application.Abstractions.Telegram;
using EncodeLabs.Application.Abstractions.Tunnelling;
using EncodeLabs.Infrastructure.Messaging.RabbitMq;
using EncodeLabs.Infrastructure.Persistence;
using EncodeLabs.Infrastructure.Persistence.Repositories;
using EncodeLabs.Infrastructure.Telegram;
using EncodeLabs.Infrastructure.Tunnelling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Telegram.Bot;

namespace EncodeLabs.Infrastructure;

public static class InfrastructureServiceRegistration
{
    private static readonly TimeSpan NgrokAgentRequestTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan TelegramApiRequestTimeout = TimeSpan.FromSeconds(30);

    public static IServiceCollection AddInboxInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddValidatedOptions<DatabaseOptions>(configuration, DatabaseOptions.ConfigurationSectionName);
        services.AddValidatedOptions<RabbitMqOptions>(configuration, RabbitMqOptions.ConfigurationSectionName);
        services.AddValidatedOptions<TelegramBotOptions>(configuration, TelegramBotOptions.ConfigurationSectionName);
        services.AddValidatedOptions<NgrokOptions>(configuration, NgrokOptions.ConfigurationSectionName);

        services.AddPersistence();
        services.AddRabbitMqMessaging();
        services.AddTelegramBotApi();
        services.AddNgrokTunnelDiscovery();

        return services;
    }

    private static IServiceCollection AddPersistence(this IServiceCollection services)
    {
        services.AddDbContext<InboxDbContext>((serviceProvider, dbContextOptions) =>
        {
            DatabaseOptions databaseOptions = serviceProvider
                .GetRequiredService<IOptions<DatabaseOptions>>().Value;

            dbContextOptions.UseNpgsql(
                databaseOptions.ConnectionString,
                // Survives the short connection drops that happen when PostgreSQL restarts.
                npgsqlOptions => npgsqlOptions.EnableRetryOnFailure());
        });

        services.AddScoped<IContactRepository, ContactRepository>();
        services.AddScoped<IMessageRepository, MessageRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<InboxDatabaseInitialiser>();

        return services;
    }

    private static IServiceCollection AddRabbitMqMessaging(this IServiceCollection services)
    {
        // Connection and publisher channel are long-lived and shared; the consumer runs as a hosted
        // service inside this same host, as required by the single-deployable constraint.
        services.AddSingleton<RabbitMqConnectionProvider>();
        services.AddSingleton<RabbitMqIncomingMessagePublisher>();
        services.AddSingleton<IIncomingMessageQueuePublisher>(serviceProvider =>
            serviceProvider.GetRequiredService<RabbitMqIncomingMessagePublisher>());
        services.AddHostedService<IncomingTelegramMessageConsumerService>();

        return services;
    }

    private static IServiceCollection AddTelegramBotApi(this IServiceCollection services)
    {
        services
            .AddHttpClient(nameof(ITelegramBotClient), httpClient => httpClient.Timeout = TelegramApiRequestTimeout)
            .AddTypedClient<ITelegramBotClient>((httpClient, serviceProvider) =>
            {
                TelegramBotOptions telegramBotOptions = serviceProvider
                    .GetRequiredService<IOptions<TelegramBotOptions>>().Value;

                return new TelegramBotClient(telegramBotOptions.BotToken, httpClient);
            });

        services.AddTransient<TelegramBotApiClient>();
        services.AddTransient<ITelegramReplySender>(serviceProvider =>
            serviceProvider.GetRequiredService<TelegramBotApiClient>());
        services.AddTransient<ITelegramWebhookRegistrar>(serviceProvider =>
            serviceProvider.GetRequiredService<TelegramBotApiClient>());

        return services;
    }

    private static IServiceCollection AddNgrokTunnelDiscovery(this IServiceCollection services)
    {
        services.AddHttpClient<IPublicTunnelUrlProvider, NgrokTunnelUrlProvider>(
            (serviceProvider, httpClient) =>
            {
                NgrokOptions ngrokOptions = serviceProvider.GetRequiredService<IOptions<NgrokOptions>>().Value;
                httpClient.BaseAddress = ngrokOptions.AgentApiBaseAddress;
                httpClient.Timeout = NgrokAgentRequestTimeout;
            });

        return services;
    }

    private static IServiceCollection AddValidatedOptions<TOptions>(
        this IServiceCollection services,
        IConfiguration configuration,
        string configurationSectionName)
        where TOptions : class
    {
        // ValidateOnStart turns a missing token or connection string into an immediate, explicit
        // start-up failure instead of an obscure error on the first incoming message.
        services.AddOptions<TOptions>()
            .Bind(configuration.GetSection(configurationSectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }
}

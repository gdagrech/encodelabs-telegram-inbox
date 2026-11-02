using EncodeLabs.Application.Inbox.UseCases;
using Microsoft.Extensions.DependencyInjection;

namespace EncodeLabs.Application;

public static class ApplicationServiceRegistration
{
    /// <summary>
    /// Registers the use-case handlers. They are plain classes resolved directly by the endpoints and
    /// the queue consumer; no mediator or pipeline indirection is needed at this size.
    /// </summary>
    public static IServiceCollection AddInboxUseCases(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);

        services.AddScoped<ListContactSummariesHandler>();
        services.AddScoped<GetConversationHistoryHandler>();
        services.AddScoped<MarkConversationAsReadHandler>();
        services.AddScoped<SendOperatorReplyHandler>();
        services.AddScoped<ProcessIncomingTelegramMessageHandler>();

        return services;
    }
}

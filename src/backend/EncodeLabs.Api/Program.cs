using System.Text.Json;
using System.Text.Json.Serialization;
using EncodeLabs.Api.BackgroundServices;
using EncodeLabs.Api.Endpoints;
using EncodeLabs.Api.ErrorHandling;
using EncodeLabs.Api.Realtime;
using EncodeLabs.Application;
using EncodeLabs.Application.Abstractions.Realtime;
using EncodeLabs.Infrastructure;
using EncodeLabs.Infrastructure.Persistence;
using EncodeLabs.Infrastructure.Telegram;
using Microsoft.Extensions.Options;

// Webhook payloads and operator replies are both small; a tight limit keeps the publicly reachable
// endpoint from being used to push large bodies into the process.
const long maximumRequestBodySizeInBytes = 256 * 1024;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(kestrelOptions =>
    kestrelOptions.Limits.MaxRequestBodySize = maximumRequestBodySizeInBytes);

builder.Services.AddInboxUseCases();
builder.Services.AddInboxInfrastructure(builder.Configuration);

// SignalR lives in the web host, so the realtime port is implemented here rather than in Infrastructure.
builder.Services.AddSingleton<IInboxRealtimeNotifier, SignalRInboxRealtimeNotifier>();
builder.Services
    .AddSignalR()
    .AddJsonProtocol(signalROptions =>
    {
        // Stated explicitly so hub payloads are guaranteed to match the camel-case HTTP responses
        // the Angular models are typed against.
        signalROptions.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        signalROptions.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.ConfigureHttpJsonOptions(jsonOptions =>
    jsonOptions.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddHostedService<TelegramWebhookRegistrationService>();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<InboxExceptionHandler>();

WebApplication app = builder.Build();

await ApplyDatabaseMigrationsAsync(app);

app.UseExceptionHandler();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .WithName("GetHealth")
    .WithSummary("Liveness probe used by Docker Compose and by the webhook registration service.");

app.MapInboxEndpoints();
app.MapTelegramWebhookEndpoints(
    app.Services.GetRequiredService<IOptions<TelegramBotOptions>>().Value.WebhookPath);
app.MapHub<InboxHub>(InboxHub.RoutePattern);

await app.RunAsync();

static async Task ApplyDatabaseMigrationsAsync(WebApplication application)
{
    // Migrating before the host starts serving guarantees no request is handled against an old schema.
    using IServiceScope startupScope = application.Services.CreateScope();
    InboxDatabaseInitialiser databaseInitialiser =
        startupScope.ServiceProvider.GetRequiredService<InboxDatabaseInitialiser>();

    await databaseInitialiser.ApplyMigrationsAsync(application.Lifetime.ApplicationStopping);
}

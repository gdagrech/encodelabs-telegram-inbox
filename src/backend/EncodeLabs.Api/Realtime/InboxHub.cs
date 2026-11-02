using Microsoft.AspNetCore.SignalR;

namespace EncodeLabs.Api.Realtime;

/// <summary>
/// Push-only hub for the inbox. It exposes no server methods: the client sends everything over HTTP
/// and uses this connection purely to receive updates, which keeps one command path instead of two.
/// </summary>
/// <remarks>
/// There is no authentication in scope, so every connected client is an operator looking at the same
/// shared inbox and all notifications are broadcast to all clients. Introducing per-operator groups
/// would only make sense together with authentication.
/// </remarks>
public sealed class InboxHub : Hub
{
    public const string RoutePattern = "/hubs/inbox";
}

using EncodeLabs.Api.Contracts;
using EncodeLabs.Application.Inbox.ReadModels;
using EncodeLabs.Application.Inbox.UseCases;

namespace EncodeLabs.Api.Endpoints;

/// <summary>
/// HTTP surface the Angular inbox uses. The application read models double as response contracts:
/// they contain exactly what the UI renders, so an extra mapping layer would add no information.
/// </summary>
public static class InboxEndpoints
{
    public static IEndpointRouteBuilder MapInboxEndpoints(this IEndpointRouteBuilder endpointRouteBuilder)
    {
        RouteGroupBuilder contactsGroup = endpointRouteBuilder
            .MapGroup("/api/contacts")
            .WithTags("Inbox");

        // An empty pattern maps exactly "/api/contacts"; "/" would append a trailing slash.
        contactsGroup
            .MapGet("", ListContactsAsync)
            .WithName("ListContacts")
            .WithSummary("Lists contacts ordered by last activity, with unread counts and last message preview.");

        contactsGroup
            .MapGet("/{contactId:guid}/messages", GetConversationAsync)
            .WithName("GetConversation")
            .WithSummary("Returns the full message history of one conversation, oldest first.");

        contactsGroup
            .MapPost("/{contactId:guid}/messages", SendReplyAsync)
            .WithName("SendReply")
            .WithSummary("Delivers an operator reply through Telegram and stores it once Telegram acknowledges it.");

        contactsGroup
            .MapPost("/{contactId:guid}/read-state", MarkConversationAsReadAsync)
            .WithName("MarkConversationAsRead")
            .WithSummary("Marks incoming messages up to the given sequence number as read.");

        return endpointRouteBuilder;
    }

    private static async Task<IResult> ListContactsAsync(
        ListContactSummariesHandler listContactSummariesHandler,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ContactSummary> contactSummaries =
            await listContactSummariesHandler.HandleAsync(cancellationToken);

        return Results.Ok(contactSummaries);
    }

    private static async Task<IResult> GetConversationAsync(
        Guid contactId,
        GetConversationHistoryHandler getConversationHistoryHandler,
        CancellationToken cancellationToken)
    {
        ConversationHistory conversationHistory =
            await getConversationHistoryHandler.HandleAsync(contactId, cancellationToken);

        return Results.Ok(conversationHistory);
    }

    private static async Task<IResult> SendReplyAsync(
        Guid contactId,
        SendReplyRequest sendReplyRequest,
        SendOperatorReplyHandler sendOperatorReplyHandler,
        CancellationToken cancellationToken)
    {
        MessageView sentMessage = await sendOperatorReplyHandler.HandleAsync(
            contactId,
            sendReplyRequest.Text,
            cancellationToken);

        return Results.Ok(sentMessage);
    }

    private static async Task<IResult> MarkConversationAsReadAsync(
        Guid contactId,
        MarkConversationAsReadRequest markConversationAsReadRequest,
        MarkConversationAsReadHandler markConversationAsReadHandler,
        CancellationToken cancellationToken)
    {
        ConversationReadState conversationReadState = await markConversationAsReadHandler.HandleAsync(
            contactId,
            markConversationAsReadRequest.ReadUpToSequenceNumber,
            cancellationToken);

        return Results.Ok(conversationReadState);
    }
}

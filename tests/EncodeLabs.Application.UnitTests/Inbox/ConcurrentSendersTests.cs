using EncodeLabs.Application.Abstractions.Messaging;
using EncodeLabs.Application.Inbox.UseCases;
using EncodeLabs.Application.UnitTests.Doubles;
using EncodeLabs.Domain.Contacts;
using EncodeLabs.Domain.Messages;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EncodeLabs.Application.UnitTests.Inbox;

/// <summary>
/// Several people writing to the same bot must land in separate conversations.
/// </summary>
/// <remarks>
/// This is the behaviour a shared inbox cannot get wrong: showing one contact's message under
/// another contact's name, or silently dropping it, is worse than losing it outright. It is covered
/// here rather than end-to-end because reproducing it live needs several real Telegram accounts.
/// </remarks>
public sealed class ConcurrentSendersTests
{
    private static readonly DateTimeOffset ArbitraryMoment =
        new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    private static ProcessIncomingTelegramMessageHandler CreateHandler(InMemoryInboxStore inboxStore) =>
        new(
            inboxStore.ContactRepository,
            inboxStore.MessageRepository,
            inboxStore.UnitOfWork,
            inboxStore.RealtimeNotifier,
            NullLogger<ProcessIncomingTelegramMessageHandler>.Instance);

    private static IncomingTelegramMessage MessageFrom(
        long telegramUserId,
        long telegramMessageId,
        string text,
        string firstName = "Sender",
        int minutesOffset = 0) =>
        new(
            UpdateId: telegramUserId * 1000 + telegramMessageId,
            TelegramMessageId: telegramMessageId,
            TelegramUserId: telegramUserId,
            TelegramChatId: telegramUserId,
            FirstName: firstName,
            LastName: null,
            Username: null,
            Text: text,
            SentAtUtc: ArbitraryMoment.AddMinutes(minutesOffset));

    [Fact]
    public async Task ThreeSenders_EachGetTheirOwnContactAndTheirOwnMessages()
    {
        InMemoryInboxStore inboxStore = new();
        ProcessIncomingTelegramMessageHandler handler = CreateHandler(inboxStore);

        await handler.HandleAsync(MessageFrom(101, 1, "soy Ana", "Ana"), CancellationToken.None);
        await handler.HandleAsync(MessageFrom(202, 1, "soy Bruno", "Bruno", 1), CancellationToken.None);
        await handler.HandleAsync(MessageFrom(303, 1, "soy Carla", "Carla", 2), CancellationToken.None);

        Assert.Equal(3, inboxStore.Contacts.Count);

        foreach ((long telegramUserId, string expectedText) in new[]
                 {
                     (101L, "soy Ana"), (202L, "soy Bruno"), (303L, "soy Carla")
                 })
        {
            Contact contact = inboxStore.Contacts.Single(candidate => candidate.TelegramUserId == telegramUserId);
            Message onlyMessage = Assert.Single(inboxStore.MessagesOf(contact.Id));
            Assert.Equal(expectedText, onlyMessage.Text);
        }
    }

    /// <summary>
    /// Telegram numbers messages per chat, so two senders routinely produce the same
    /// <c>message_id</c>. If deduplication were not scoped to the contact, the second sender's
    /// message would be discarded as a duplicate of the first and never reach the inbox.
    /// </summary>
    [Fact]
    public async Task SameTelegramMessageIdFromDifferentSenders_IsNotTreatedAsADuplicate()
    {
        InMemoryInboxStore inboxStore = new();
        ProcessIncomingTelegramMessageHandler handler = CreateHandler(inboxStore);

        IncomingMessageProcessingOutcome firstOutcome = await handler.HandleAsync(
            MessageFrom(101, 7, "mensaje de Ana", "Ana"),
            CancellationToken.None);

        IncomingMessageProcessingOutcome secondOutcome = await handler.HandleAsync(
            MessageFrom(202, 7, "mensaje de Bruno", "Bruno", 1),
            CancellationToken.None);

        Assert.Equal(IncomingMessageProcessingOutcome.Stored, firstOutcome);
        Assert.Equal(IncomingMessageProcessingOutcome.Stored, secondOutcome);
        Assert.Equal(2, inboxStore.Messages.Count);
    }

    /// <summary>The same sender repeating a message id is a redelivery, and must be discarded.</summary>
    [Fact]
    public async Task SameTelegramMessageIdFromTheSameSender_IsDiscardedAsADuplicate()
    {
        InMemoryInboxStore inboxStore = new();
        ProcessIncomingTelegramMessageHandler handler = CreateHandler(inboxStore);

        await handler.HandleAsync(MessageFrom(101, 7, "hola", "Ana"), CancellationToken.None);

        IncomingMessageProcessingOutcome redeliveryOutcome = await handler.HandleAsync(
            MessageFrom(101, 7, "hola", "Ana"),
            CancellationToken.None);

        Assert.Equal(IncomingMessageProcessingOutcome.DuplicateDiscarded, redeliveryOutcome);
        Assert.Single(inboxStore.Messages);
    }

    [Fact]
    public async Task AFurtherMessageFromAKnownSender_ReusesTheContactInsteadOfCreatingAnother()
    {
        InMemoryInboxStore inboxStore = new();
        ProcessIncomingTelegramMessageHandler handler = CreateHandler(inboxStore);

        await handler.HandleAsync(MessageFrom(101, 1, "primero", "Ana"), CancellationToken.None);
        await handler.HandleAsync(MessageFrom(202, 1, "de Bruno", "Bruno", 1), CancellationToken.None);
        await handler.HandleAsync(MessageFrom(101, 2, "segundo", "Ana", 2), CancellationToken.None);

        Contact ana = inboxStore.Contacts.Single(contact => contact.TelegramUserId == 101);

        Assert.Equal(2, inboxStore.Contacts.Count);
        Assert.Equal(["primero", "segundo"], inboxStore.MessagesOf(ana.Id).Select(message => message.Text));
    }

    /// <summary>
    /// A duplicate rejected by the unique index must not be announced: the clients already have that
    /// message, and pushing it again would show it twice until the next refresh.
    /// </summary>
    [Fact]
    public async Task ADuplicateIsNotAnnouncedOverTheRealtimeChannel()
    {
        InMemoryInboxStore inboxStore = new();
        ProcessIncomingTelegramMessageHandler handler = CreateHandler(inboxStore);

        await handler.HandleAsync(MessageFrom(101, 7, "hola", "Ana"), CancellationToken.None);
        await handler.HandleAsync(MessageFrom(101, 7, "hola", "Ana"), CancellationToken.None);

        Assert.Single(inboxStore.NotifiedMessages);
    }

    /// <summary>
    /// Nothing is announced when the commit fails, so a client can never be told about a message the
    /// database does not have.
    /// </summary>
    [Fact]
    public async Task AFailedCommitAnnouncesNothing()
    {
        InMemoryInboxStore inboxStore = new()
        {
            FailNextSaveWith = new InvalidOperationException("the database is down")
        };

        ProcessIncomingTelegramMessageHandler handler = CreateHandler(inboxStore);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(MessageFrom(101, 1, "hola", "Ana"), CancellationToken.None));

        Assert.Empty(inboxStore.NotifiedMessages);
    }
}

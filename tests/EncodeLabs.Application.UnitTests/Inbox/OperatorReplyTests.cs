using EncodeLabs.Application.Abstractions.Telegram;
using EncodeLabs.Application.Inbox;
using EncodeLabs.Application.Inbox.ReadModels;
using EncodeLabs.Application.Inbox.UseCases;
using EncodeLabs.Application.UnitTests.Doubles;
using EncodeLabs.Domain.Contacts;
using EncodeLabs.Domain.Messages;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EncodeLabs.Application.UnitTests.Inbox;

public sealed class OperatorReplyTests
{
    private static readonly DateTimeOffset ArbitraryMoment =
        new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    private sealed class AcceptingTelegramSender : ITelegramReplySender
    {
        internal int SentCount { get; private set; }

        public Task<TelegramReplyDelivery> SendTextMessageAsync(
            long telegramChatId,
            string replyText,
            CancellationToken cancellationToken)
        {
            SentCount++;
            return Task.FromResult(new TelegramReplyDelivery(SentCount, ArbitraryMoment));
        }
    }

    private static Contact RegisterContact(InMemoryInboxStore inboxStore, long telegramUserId)
    {
        Contact contact = Contact.CreateFromTelegramSender(
            telegramUserId,
            telegramUserId,
            "Ana",
            null,
            null,
            ArbitraryMoment);

        inboxStore.ContactRepository.Add(contact);
        return contact;
    }

    private static SendOperatorReplyHandler CreateHandler(
        InMemoryInboxStore inboxStore,
        ITelegramReplySender telegramReplySender) =>
        new(
            inboxStore.ContactRepository,
            inboxStore.MessageRepository,
            inboxStore.UnitOfWork,
            telegramReplySender,
            inboxStore.RealtimeNotifier,
            NullLogger<SendOperatorReplyHandler>.Instance);

    [Fact]
    public async Task AnAcceptedReplyIsStoredAsOutgoingAndAlreadyRead()
    {
        InMemoryInboxStore inboxStore = new();
        Contact contact = RegisterContact(inboxStore, 101);
        SendOperatorReplyHandler handler = CreateHandler(inboxStore, new AcceptingTelegramSender());

        await handler.HandleAsync(contact.Id, "respuesta", CancellationToken.None);

        Message storedReply = Assert.Single(inboxStore.MessagesOf(contact.Id));
        Assert.Equal(MessageDirection.Outgoing, storedReply.Direction);
        Assert.True(storedReply.IsRead);
    }

    /// <summary>
    /// Telegram delivered the text and only our record of it is missing. Reporting this as a plain
    /// failure would invite a retry, and the contact would receive the message twice — so it gets
    /// its own exception, which the interface turns into "do not send it again".
    /// </summary>
    [Fact]
    public async Task AReplyTelegramDeliveredButTheDatabaseRejected_IsReportedAsDeliveredNotFailed()
    {
        InMemoryInboxStore inboxStore = new()
        {
            FailNextSaveWith = new InvalidOperationException("the database is down")
        };

        Contact contact = RegisterContact(inboxStore, 101);
        AcceptingTelegramSender telegramSender = new();
        SendOperatorReplyHandler handler = CreateHandler(inboxStore, telegramSender);

        ReplyDeliveredButNotRecordedException thrownException =
            await Assert.ThrowsAsync<ReplyDeliveredButNotRecordedException>(() =>
                handler.HandleAsync(contact.Id, "respuesta", CancellationToken.None));

        Assert.Equal(1, telegramSender.SentCount);
        Assert.Equal(1, thrownException.TelegramMessageId);
        Assert.Empty(inboxStore.NotifiedMessages);
    }

    [Fact]
    public async Task ReplyingToAnUnknownContactIsRejectedBeforeTelegramIsCalled()
    {
        InMemoryInboxStore inboxStore = new();
        AcceptingTelegramSender telegramSender = new();
        SendOperatorReplyHandler handler = CreateHandler(inboxStore, telegramSender);

        await Assert.ThrowsAsync<ContactNotFoundException>(() =>
            handler.HandleAsync(Guid.NewGuid(), "respuesta", CancellationToken.None));

        Assert.Equal(0, telegramSender.SentCount);
    }
}

public sealed class ContactSummaryPreviewTests
{
    [Fact]
    public void AShortMessageIsPreviewedWhole()
    {
        Assert.Equal("hola", ContactSummary.BuildPreview("hola"));
    }

    [Fact]
    public void ALongMessageIsTruncatedWithAnEllipsis()
    {
        string preview = ContactSummary.BuildPreview(new string('a', 400));

        Assert.Equal(ContactSummary.MaximumPreviewLength + 1, preview.Length);
        Assert.EndsWith("…", preview, StringComparison.Ordinal);
    }

    /// <summary>
    /// An emoji occupies two <see cref="char"/> values. Cutting between them yields a lone surrogate
    /// that renders as "�", which reads as corrupted storage rather than as a truncated preview.
    /// </summary>
    [Fact]
    public void AnEmojiStraddlingTheCutIsDroppedRatherThanSplit()
    {
        // The emoji starts at the last position inside the limit, so a naive cut would split it.
        string messageText = new string('a', ContactSummary.MaximumPreviewLength - 1) + "😀 y más texto";

        string preview = ContactSummary.BuildPreview(messageText);

        Assert.DoesNotContain('�', preview);
        Assert.False(char.IsHighSurrogate(preview[^2]));
    }

    [Fact]
    public void AnEmojiThatFitsEntirelyInsideTheLimitIsKept()
    {
        string messageText = new string('a', ContactSummary.MaximumPreviewLength - 2) + "😀 y más texto";

        string preview = ContactSummary.BuildPreview(messageText);

        Assert.Contains("😀", preview, StringComparison.Ordinal);
        Assert.DoesNotContain('�', preview);
    }
}

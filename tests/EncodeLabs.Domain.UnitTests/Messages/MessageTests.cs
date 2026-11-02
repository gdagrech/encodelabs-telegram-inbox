using EncodeLabs.Domain.Common;
using EncodeLabs.Domain.Messages;
using Xunit;

namespace EncodeLabs.Domain.UnitTests.Messages;

public sealed class MessageTests
{
    private const long TelegramMessageId = 42;

    private static readonly Guid ContactId = Guid.CreateVersion7();
    private static readonly DateTimeOffset SentAtUtc = new(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateIncoming_StartsUnread_SoItIsCountedInTheUnreadBadge()
    {
        Message incomingMessage = CreateIncomingMessage();

        Assert.Equal(MessageDirection.Incoming, incomingMessage.Direction);
        Assert.False(incomingMessage.IsRead);
        Assert.Null(incomingMessage.ReadAtUtc);
    }

    [Fact]
    public void CreateOutgoing_IsAlreadyRead_BecauseTheOperatorWroteIt()
    {
        Message outgoingMessage = Message.CreateOutgoing(
            ContactId,
            TelegramMessageId,
            MessageText.Create("On my way."),
            SentAtUtc);

        Assert.Equal(MessageDirection.Outgoing, outgoingMessage.Direction);
        Assert.True(outgoingMessage.IsRead);
        Assert.Equal(SentAtUtc, outgoingMessage.ReadAtUtc);
    }

    [Fact]
    public void MarkAsRead_RecordsTheReadTimestampAndReportsTheChange()
    {
        Message incomingMessage = CreateIncomingMessage();
        DateTimeOffset readAtUtc = SentAtUtc.AddMinutes(1);

        bool readStateChanged = incomingMessage.MarkAsRead(readAtUtc);

        Assert.True(readStateChanged);
        Assert.True(incomingMessage.IsRead);
        Assert.Equal(readAtUtc, incomingMessage.ReadAtUtc);
    }

    [Fact]
    public void MarkAsRead_ReportsNoChange_WhenTheMessageWasAlreadyRead()
    {
        Message incomingMessage = CreateIncomingMessage();
        DateTimeOffset firstReadAtUtc = SentAtUtc.AddMinutes(1);
        incomingMessage.MarkAsRead(firstReadAtUtc);

        bool readStateChanged = incomingMessage.MarkAsRead(SentAtUtc.AddMinutes(2));

        Assert.False(readStateChanged);
        Assert.Equal(firstReadAtUtc, incomingMessage.ReadAtUtc);
    }

    [Fact]
    public void MarkAsRead_IsRejectedForAnOutgoingMessage()
    {
        Message outgoingMessage = Message.CreateOutgoing(
            ContactId,
            TelegramMessageId,
            MessageText.Create("On my way."),
            SentAtUtc);

        Assert.Throws<DomainRuleViolationException>(() => outgoingMessage.MarkAsRead(SentAtUtc.AddMinutes(1)));
    }

    [Fact]
    public void CreateIncoming_RejectsAMissingContact()
    {
        Assert.Throws<DomainRuleViolationException>(() => Message.CreateIncoming(
            Guid.Empty,
            TelegramMessageId,
            MessageText.Create("Hello"),
            SentAtUtc));
    }

    [Fact]
    public void CreateIncoming_RejectsANonPositiveTelegramMessageId()
    {
        Assert.Throws<DomainRuleViolationException>(() => Message.CreateIncoming(
            ContactId,
            telegramMessageId: 0,
            MessageText.Create("Hello"),
            SentAtUtc));
    }

    private static Message CreateIncomingMessage() => Message.CreateIncoming(
        ContactId,
        TelegramMessageId,
        MessageText.Create("Hello, is anybody there?"),
        SentAtUtc);
}

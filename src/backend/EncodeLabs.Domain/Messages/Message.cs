using EncodeLabs.Domain.Common;

namespace EncodeLabs.Domain.Messages;

/// <summary>
/// A single text message belonging to one conversation.
/// </summary>
public sealed class Message
{
    private Message()
    {
        // Required by EF Core materialisation.
    }

    public Guid Id { get; private set; }

    public Guid ContactId { get; private set; }

    /// <summary>
    /// Monotonic arrival order assigned by the database on insert. Telegram timestamps have
    /// one-second resolution and can tie or arrive out of order, so this value — not
    /// <see cref="SentAtUtc"/> — is what makes "mark everything the operator has seen as read"
    /// an exact, race-free cut-off.
    /// </summary>
    public long SequenceNumber { get; private set; }

    public MessageDirection Direction { get; private set; }

    public string Text { get; private set; } = string.Empty;

    /// <summary>
    /// Telegram identifier of the message (<c>message.message_id</c>), unique within a chat for both
    /// directions. Combined with the contact it is the natural key used to discard duplicate
    /// webhook deliveries.
    /// </summary>
    public long TelegramMessageId { get; private set; }

    public DateTimeOffset SentAtUtc { get; private set; }

    /// <summary>
    /// Whether the operator has seen this message. Outgoing messages are read on creation because
    /// the operator wrote them, which keeps the unread query a single predicate.
    /// </summary>
    public bool IsRead { get; private set; }

    public DateTimeOffset? ReadAtUtc { get; private set; }

    public static Message CreateIncoming(
        Guid contactId,
        long telegramMessageId,
        MessageText text,
        DateTimeOffset sentAtUtc) =>
        Create(contactId, MessageDirection.Incoming, telegramMessageId, text, sentAtUtc, isRead: false, readAtUtc: null);

    public static Message CreateOutgoing(
        Guid contactId,
        long telegramMessageId,
        MessageText text,
        DateTimeOffset sentAtUtc) =>
        Create(contactId, MessageDirection.Outgoing, telegramMessageId, text, sentAtUtc, isRead: true, readAtUtc: sentAtUtc);

    /// <summary>
    /// Marks an incoming message as read. Returns <see langword="false"/> when nothing changed, so
    /// callers can avoid publishing read-state notifications that carry no information.
    /// </summary>
    public bool MarkAsRead(DateTimeOffset readAtUtc)
    {
        if (Direction != MessageDirection.Incoming)
        {
            throw new DomainRuleViolationException("Only incoming messages carry an operator read state.");
        }

        if (IsRead)
        {
            return false;
        }

        IsRead = true;
        ReadAtUtc = readAtUtc;
        return true;
    }

    private static Message Create(
        Guid contactId,
        MessageDirection direction,
        long telegramMessageId,
        MessageText text,
        DateTimeOffset sentAtUtc,
        bool isRead,
        DateTimeOffset? readAtUtc)
    {
        if (contactId == Guid.Empty)
        {
            throw new DomainRuleViolationException("A message must belong to a contact.");
        }

        if (telegramMessageId <= 0)
        {
            throw new DomainRuleViolationException("A message requires a positive Telegram message identifier.");
        }

        ArgumentNullException.ThrowIfNull(text);

        return new Message
        {
            Id = Guid.CreateVersion7(),
            ContactId = contactId,
            Direction = direction,
            TelegramMessageId = telegramMessageId,
            Text = text.Value,
            SentAtUtc = sentAtUtc,
            IsRead = isRead,
            ReadAtUtc = readAtUtc
        };
    }
}

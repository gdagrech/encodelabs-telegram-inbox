using EncodeLabs.Domain.Messages;

namespace EncodeLabs.Application.Inbox.ReadModels;

/// <summary>
/// One message as rendered in the conversation panel.
/// </summary>
public sealed record MessageView(
    Guid MessageId,
    long SequenceNumber,
    MessageDirection Direction,
    string Text,
    DateTimeOffset SentAtUtc,
    bool IsRead)
{
    public static MessageView FromMessage(Message message) => new(
        message.Id,
        message.SequenceNumber,
        message.Direction,
        message.Text,
        message.SentAtUtc,
        message.IsRead);
}

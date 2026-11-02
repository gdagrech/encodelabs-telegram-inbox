namespace EncodeLabs.Domain.Messages;

/// <summary>
/// Direction of a message relative to the inbox operator.
/// </summary>
public enum MessageDirection
{
    /// <summary>Sent by the Telegram contact and received by the bot.</summary>
    Incoming = 1,

    /// <summary>Written by the operator in the inbox and delivered to the contact through the bot.</summary>
    Outgoing = 2
}

namespace EncodeLabs.Application.Inbox;

/// <summary>
/// Signals that the requested conversation does not exist.
/// </summary>
public sealed class ContactNotFoundException(Guid contactId)
    : Exception($"No contact exists with identifier {contactId}.")
{
    public Guid ContactId { get; } = contactId;
}

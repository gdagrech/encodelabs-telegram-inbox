using EncodeLabs.Application.Inbox.ReadModels;
using EncodeLabs.Domain.Contacts;

namespace EncodeLabs.Application.Abstractions.Persistence;

public interface IContactRepository
{
    Task<Contact?> FindByTelegramUserIdAsync(long telegramUserId, CancellationToken cancellationToken);

    Task<Contact?> FindByIdAsync(Guid contactId, CancellationToken cancellationToken);

    void Add(Contact contact);

    /// <summary>Contact list for the side panel, most recent activity first.</summary>
    Task<IReadOnlyList<ContactSummary>> ListSummariesByLastActivityAsync(CancellationToken cancellationToken);

    /// <summary>Single contact list row, used to keep connected clients in sync after a change.</summary>
    Task<ContactSummary?> GetSummaryAsync(Guid contactId, CancellationToken cancellationToken);
}

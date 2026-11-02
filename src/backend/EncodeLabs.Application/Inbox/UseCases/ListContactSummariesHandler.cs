using EncodeLabs.Application.Abstractions.Persistence;
using EncodeLabs.Application.Inbox.ReadModels;

namespace EncodeLabs.Application.Inbox.UseCases;

/// <summary>
/// Builds the contact side panel: every known contact with its unread counter and last message preview.
/// </summary>
public sealed class ListContactSummariesHandler(IContactRepository contactRepository)
{
    public Task<IReadOnlyList<ContactSummary>> HandleAsync(CancellationToken cancellationToken) =>
        contactRepository.ListSummariesByLastActivityAsync(cancellationToken);
}

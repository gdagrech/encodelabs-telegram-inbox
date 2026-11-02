using EncodeLabs.Application.Abstractions.Persistence;
using EncodeLabs.Application.Inbox.ReadModels;
using EncodeLabs.Domain.Contacts;
using EncodeLabs.Domain.Messages;
using Microsoft.EntityFrameworkCore;

namespace EncodeLabs.Infrastructure.Persistence.Repositories;

public sealed class ContactRepository(InboxDbContext dbContext) : IContactRepository
{
    public Task<Contact?> FindByTelegramUserIdAsync(long telegramUserId, CancellationToken cancellationToken) =>
        dbContext.Contacts.SingleOrDefaultAsync(
            contact => contact.TelegramUserId == telegramUserId,
            cancellationToken);

    public Task<Contact?> FindByIdAsync(Guid contactId, CancellationToken cancellationToken) =>
        dbContext.Contacts.SingleOrDefaultAsync(contact => contact.Id == contactId, cancellationToken);

    public void Add(Contact contact) => dbContext.Contacts.Add(contact);

    public async Task<IReadOnlyList<ContactSummary>> ListSummariesByLastActivityAsync(
        CancellationToken cancellationToken)
    {
        List<ContactSummaryProjection> projections = await BuildSummaryQuery(
                dbContext.Contacts.OrderByDescending(contact => contact.LastActivityAtUtc))
            .ToListAsync(cancellationToken);

        return [.. projections.Select(ToContactSummary)];
    }

    public async Task<ContactSummary?> GetSummaryAsync(Guid contactId, CancellationToken cancellationToken)
    {
        // Filtering before projecting keeps the predicate on the contact table itself.
        ContactSummaryProjection? projection = await BuildSummaryQuery(
                dbContext.Contacts.Where(contact => contact.Id == contactId))
            .SingleOrDefaultAsync(cancellationToken);

        return projection is null ? null : ToContactSummary(projection);
    }

    /// <summary>
    /// Unread counters and the last message preview are derived with correlated sub-queries instead of
    /// being denormalised onto the contact row, so there is exactly one source of truth for both.
    /// </summary>
    private IQueryable<ContactSummaryProjection> BuildSummaryQuery(IQueryable<Contact> contactQuery) =>
        contactQuery
            .AsNoTracking()
            .Select(contact => new ContactSummaryProjection
            {
                Contact = contact,
                UnreadMessageCount = dbContext.Messages.Count(message =>
                    message.ContactId == contact.Id
                    && message.Direction == MessageDirection.Incoming
                    && !message.IsRead),
                LastMessage = dbContext.Messages
                    .Where(message => message.ContactId == contact.Id)
                    .OrderByDescending(message => message.SequenceNumber)
                    .Select(message => new LastMessageProjection
                    {
                        Text = message.Text,
                        Direction = message.Direction
                    })
                    .FirstOrDefault()
            });

    private static ContactSummary ToContactSummary(ContactSummaryProjection projection) => new(
        projection.Contact.Id,
        projection.Contact.DisplayName,
        projection.Contact.Username,
        projection.UnreadMessageCount,
        projection.LastMessage is null ? null : ContactSummary.BuildPreview(projection.LastMessage.Text),
        projection.LastMessage?.Direction,
        projection.Contact.LastActivityAtUtc);

    private sealed class ContactSummaryProjection
    {
        public required Contact Contact { get; init; }

        public required int UnreadMessageCount { get; init; }

        public required LastMessageProjection? LastMessage { get; init; }
    }

    private sealed class LastMessageProjection
    {
        public required string Text { get; init; }

        public required MessageDirection Direction { get; init; }
    }
}

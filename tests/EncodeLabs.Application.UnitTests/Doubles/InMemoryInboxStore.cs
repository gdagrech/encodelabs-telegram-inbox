using EncodeLabs.Application.Abstractions.Persistence;
using EncodeLabs.Application.Abstractions.Realtime;
using EncodeLabs.Application.Inbox.ReadModels;
using EncodeLabs.Domain.Contacts;
using EncodeLabs.Domain.Messages;

namespace EncodeLabs.Application.UnitTests.Doubles;

/// <summary>
/// Hand-written stand-ins for the persistence and realtime ports, sharing one in-memory store.
/// </summary>
/// <remarks>
/// These doubles deliberately do **not** reimplement the database's guarantees: there is no unique
/// index here, and the dedup lookup is a plain scan. What they make testable is the handler's own
/// logic — which contact a message is attached to, and which identifiers it is compared against.
/// The guarantees that live in PostgreSQL are verified against PostgreSQL, not against this file.
/// </remarks>
internal sealed class InMemoryInboxStore
{
    private readonly List<Contact> contacts = [];
    private readonly List<Message> messages = [];

    internal IReadOnlyList<Contact> Contacts => contacts;

    internal IReadOnlyList<Message> Messages => messages;

    /// <summary>Messages announced over the realtime port, in the order they were announced.</summary>
    internal List<MessageView> NotifiedMessages { get; } = [];

    /// <summary>Set to make the next commit fail, standing in for a database that just went down.</summary>
    internal Exception? FailNextSaveWith { get; set; }

    internal IReadOnlyList<Message> MessagesOf(Guid contactId) =>
        messages.Where(message => message.ContactId == contactId).ToList();

    internal ContactRepositoryDouble ContactRepository => new(this, contacts);

    internal MessageRepositoryDouble MessageRepository => new(messages);

    internal UnitOfWorkDouble UnitOfWork => new(this);

    internal RealtimeNotifierDouble RealtimeNotifier => new(this);

    private ContactSummary? BuildSummary(Guid contactId)
    {
        Contact? contact = contacts.SingleOrDefault(candidate => candidate.Id == contactId);
        if (contact is null)
        {
            return null;
        }

        Message? lastMessage = messages
            .Where(message => message.ContactId == contactId)
            .OrderBy(message => message.SentAtUtc)
            .LastOrDefault();

        return new ContactSummary(
            contact.Id,
            contact.DisplayName,
            contact.Username,
            messages.Count(message =>
                message.ContactId == contactId
                && message.Direction == MessageDirection.Incoming
                && !message.IsRead),
            lastMessage is null ? null : ContactSummary.BuildPreview(lastMessage.Text),
            lastMessage?.Direction,
            contact.LastActivityAtUtc);
    }

    internal sealed class ContactRepositoryDouble(InMemoryInboxStore owner, List<Contact> contacts)
        : IContactRepository
    {
        public Task<Contact?> FindByTelegramUserIdAsync(long telegramUserId, CancellationToken cancellationToken) =>
            Task.FromResult(contacts.SingleOrDefault(contact => contact.TelegramUserId == telegramUserId));

        public Task<Contact?> FindByIdAsync(Guid contactId, CancellationToken cancellationToken) =>
            Task.FromResult(contacts.SingleOrDefault(contact => contact.Id == contactId));

        public void Add(Contact contact) => contacts.Add(contact);

        public Task<IReadOnlyList<ContactSummary>> ListSummariesByLastActivityAsync(CancellationToken cancellationToken)
        {
            IReadOnlyList<ContactSummary> summaries = contacts
                .OrderByDescending(contact => contact.LastActivityAtUtc)
                .Select(contact => owner.BuildSummary(contact.Id)!)
                .ToList();

            return Task.FromResult(summaries);
        }

        public Task<ContactSummary?> GetSummaryAsync(Guid contactId, CancellationToken cancellationToken) =>
            Task.FromResult(owner.BuildSummary(contactId));
    }

    internal sealed class MessageRepositoryDouble(List<Message> messages) : IMessageRepository
    {
        public void Add(Message message) => messages.Add(message);

        public Task<bool> ExistsForTelegramMessageIdAsync(
            Guid contactId,
            long telegramMessageId,
            CancellationToken cancellationToken) =>
            Task.FromResult(messages.Any(message =>
                message.ContactId == contactId && message.TelegramMessageId == telegramMessageId));

        public Task<IReadOnlyList<Message>> ListByContactChronologicallyAsync(
            Guid contactId,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<Message> conversation = messages
                .Where(message => message.ContactId == contactId)
                .OrderBy(message => message.SentAtUtc)
                .ToList();

            return Task.FromResult(conversation);
        }

        public Task<IReadOnlyList<Message>> ListUnreadIncomingUpToSequenceAsync(
            Guid contactId,
            long readUpToSequenceNumber,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<Message> unread = messages
                .Where(message =>
                    message.ContactId == contactId
                    && message.Direction == MessageDirection.Incoming
                    && !message.IsRead
                    && message.SequenceNumber <= readUpToSequenceNumber)
                .ToList();

            return Task.FromResult(unread);
        }

        public Task<int> CountUnreadIncomingAsync(Guid contactId, CancellationToken cancellationToken) =>
            Task.FromResult(messages.Count(message =>
                message.ContactId == contactId
                && message.Direction == MessageDirection.Incoming
                && !message.IsRead));
    }

    internal sealed class UnitOfWorkDouble(InMemoryInboxStore owner) : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        {
            if (owner.FailNextSaveWith is { } failure)
            {
                owner.FailNextSaveWith = null;
                throw failure;
            }

            return Task.FromResult(0);
        }
    }

    internal sealed class RealtimeNotifierDouble(InMemoryInboxStore owner) : IInboxRealtimeNotifier
    {
        public Task NotifyMessageAddedAsync(
            ContactSummary contactSummary,
            MessageView message,
            CancellationToken cancellationToken)
        {
            owner.NotifiedMessages.Add(message);
            return Task.CompletedTask;
        }

        public Task NotifyConversationReadStateChangedAsync(
            ConversationReadState conversationReadState,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

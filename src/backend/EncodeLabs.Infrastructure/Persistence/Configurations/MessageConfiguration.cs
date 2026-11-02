using EncodeLabs.Domain.Contacts;
using EncodeLabs.Domain.Messages;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EncodeLabs.Infrastructure.Persistence.Configurations;

public sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    /// <summary>
    /// Name of the index that enforces Telegram message identity inside a conversation. The unit of
    /// work matches on it to translate a unique violation into a recognised duplicate delivery.
    /// </summary>
    public const string UniqueTelegramMessageIndexName = "ix_messages_contact_id_telegram_message_id";

    public void Configure(EntityTypeBuilder<Message> messageBuilder)
    {
        messageBuilder.ToTable("messages");

        messageBuilder.HasKey(message => message.Id);

        // Assigned by PostgreSQL on insert: a gap-free-enough, strictly increasing arrival order.
        messageBuilder
            .Property(message => message.SequenceNumber)
            .ValueGeneratedOnAdd();

        messageBuilder.Property(message => message.Direction).IsRequired();

        messageBuilder
            .Property(message => message.Text)
            .HasMaxLength(MessageText.MaximumLength)
            .IsRequired();

        messageBuilder.Property(message => message.TelegramMessageId).IsRequired();
        messageBuilder.Property(message => message.SentAtUtc).IsRequired();
        messageBuilder.Property(message => message.IsRead).IsRequired();

        messageBuilder
            .HasOne<Contact>()
            .WithMany()
            .HasForeignKey(message => message.ContactId)
            .OnDelete(DeleteBehavior.Cascade);

        // Telegram message ids are unique per chat in both directions, so this is the deduplication key.
        messageBuilder
            .HasIndex(message => new { message.ContactId, message.TelegramMessageId })
            .IsUnique()
            .HasDatabaseName(UniqueTelegramMessageIndexName);

        // Supports history retrieval and the read cut-off, both ordered by sequence number.
        messageBuilder
            .HasIndex(message => new { message.ContactId, message.SequenceNumber })
            .HasDatabaseName("ix_messages_contact_id_sequence_number");

        // Supports the unread counters shown in the contact list.
        messageBuilder
            .HasIndex(message => new { message.ContactId, message.Direction, message.IsRead })
            .HasDatabaseName("ix_messages_contact_id_direction_is_read");
    }
}

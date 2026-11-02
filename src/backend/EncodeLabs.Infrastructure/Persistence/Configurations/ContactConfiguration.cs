using EncodeLabs.Domain.Contacts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EncodeLabs.Infrastructure.Persistence.Configurations;

public sealed class ContactConfiguration : IEntityTypeConfiguration<Contact>
{
    public const int MaximumTelegramNameLength = 256;

    public void Configure(EntityTypeBuilder<Contact> contactBuilder)
    {
        contactBuilder.ToTable("contacts");

        contactBuilder.HasKey(contact => contact.Id);

        contactBuilder.Property(contact => contact.TelegramUserId).IsRequired();
        contactBuilder.Property(contact => contact.TelegramChatId).IsRequired();

        contactBuilder.Property(contact => contact.FirstName).HasMaxLength(MaximumTelegramNameLength);
        contactBuilder.Property(contact => contact.LastName).HasMaxLength(MaximumTelegramNameLength);
        contactBuilder.Property(contact => contact.Username).HasMaxLength(MaximumTelegramNameLength);

        contactBuilder.Property(contact => contact.LastActivityAtUtc).IsRequired();

        // Derived from the name fields; recomputing it on read keeps a single source of truth.
        contactBuilder.Ignore(contact => contact.DisplayName);

        // One contact per Telegram user: this is what makes contact creation idempotent.
        contactBuilder
            .HasIndex(contact => contact.TelegramUserId)
            .IsUnique()
            .HasDatabaseName("ix_contacts_telegram_user_id");

        // Supports the inbox ordering (most recent activity first).
        contactBuilder
            .HasIndex(contact => contact.LastActivityAtUtc)
            .IsDescending()
            .HasDatabaseName("ix_contacts_last_activity_at_utc");
    }
}

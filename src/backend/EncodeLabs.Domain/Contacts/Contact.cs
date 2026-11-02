using EncodeLabs.Domain.Common;

namespace EncodeLabs.Domain.Contacts;

/// <summary>
/// A Telegram user who has written to the bot. Identity comes from Telegram, not from this system.
/// </summary>
public sealed class Contact
{
    private Contact()
    {
        // Required by EF Core materialisation.
    }

    public Guid Id { get; private set; }

    /// <summary>Telegram identifier of the user (<c>message.from.id</c>). Stable across chats.</summary>
    public long TelegramUserId { get; private set; }

    /// <summary>
    /// Telegram identifier of the private chat with the user (<c>message.chat.id</c>), used as the
    /// destination when the operator replies. For private chats it equals <see cref="TelegramUserId"/>,
    /// but it is stored separately because that equality is not part of the Bot API contract.
    /// </summary>
    public long TelegramChatId { get; private set; }

    public string? FirstName { get; private set; }

    public string? LastName { get; private set; }

    public string? Username { get; private set; }

    /// <summary>Timestamp of the most recent message exchanged in either direction; drives inbox ordering.</summary>
    public DateTimeOffset LastActivityAtUtc { get; private set; }

    /// <summary>
    /// Label shown in the inbox. Telegram guarantees none of the name fields individually, so the
    /// fallback chain ends with the numeric user identifier, which always exists.
    /// </summary>
    public string DisplayName => BuildDisplayName();

    public static Contact CreateFromTelegramSender(
        long telegramUserId,
        long telegramChatId,
        string? firstName,
        string? lastName,
        string? username,
        DateTimeOffset firstActivityAtUtc)
    {
        if (telegramUserId <= 0)
        {
            throw new DomainRuleViolationException("A contact requires a positive Telegram user identifier.");
        }

        return new Contact
        {
            Id = Guid.CreateVersion7(),
            TelegramUserId = telegramUserId,
            TelegramChatId = telegramChatId,
            FirstName = NormaliseOptionalText(firstName),
            LastName = NormaliseOptionalText(lastName),
            Username = NormaliseOptionalText(username),
            LastActivityAtUtc = firstActivityAtUtc
        };
    }

    /// <summary>
    /// Applies the profile fields of the latest Telegram update. Telegram is the source of truth, so a
    /// field that disappeared upstream (for example a removed username) is cleared here as well.
    /// </summary>
    public void RefreshTelegramProfile(string? firstName, string? lastName, string? username, long telegramChatId)
    {
        FirstName = NormaliseOptionalText(firstName);
        LastName = NormaliseOptionalText(lastName);
        Username = NormaliseOptionalText(username);

        if (telegramChatId != 0)
        {
            TelegramChatId = telegramChatId;
        }
    }

    /// <summary>
    /// Moves the last-activity marker forward. Out-of-order deliveries must not pull the inbox
    /// ordering backwards, so older timestamps are ignored.
    /// </summary>
    public void RegisterActivity(DateTimeOffset activityAtUtc)
    {
        if (activityAtUtc > LastActivityAtUtc)
        {
            LastActivityAtUtc = activityAtUtc;
        }
    }

    private string BuildDisplayName()
    {
        string fullName = string.Join(' ', new[] { FirstName, LastName }
            .Where(namePart => !string.IsNullOrWhiteSpace(namePart)));

        if (!string.IsNullOrWhiteSpace(fullName))
        {
            return fullName;
        }

        return !string.IsNullOrWhiteSpace(Username)
            ? $"@{Username}"
            : $"Telegram user {TelegramUserId}";
    }

    private static string? NormaliseOptionalText(string? candidateText) =>
        string.IsNullOrWhiteSpace(candidateText) ? null : candidateText.Trim();
}

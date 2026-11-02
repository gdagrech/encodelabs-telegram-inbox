using EncodeLabs.Domain.Common;
using EncodeLabs.Domain.Contacts;
using Xunit;

namespace EncodeLabs.Domain.UnitTests.Contacts;

public sealed class ContactTests
{
    private const long TelegramUserId = 123456789;
    private const long TelegramChatId = 123456789;

    private static readonly DateTimeOffset FirstActivityAtUtc = new(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void DisplayName_CombinesFirstAndLastName_WhenBothArePresent()
    {
        Contact contact = CreateContact(firstName: "Ada", lastName: "Lovelace", username: "ada");

        Assert.Equal("Ada Lovelace", contact.DisplayName);
    }

    [Fact]
    public void DisplayName_UsesFirstNameOnly_WhenLastNameIsMissing()
    {
        Contact contact = CreateContact(firstName: "Ada", lastName: null, username: "ada");

        Assert.Equal("Ada", contact.DisplayName);
    }

    [Fact]
    public void DisplayName_FallsBackToUsername_WhenNoNameIsProvided()
    {
        Contact contact = CreateContact(firstName: null, lastName: null, username: "ada");

        Assert.Equal("@ada", contact.DisplayName);
    }

    [Fact]
    public void DisplayName_FallsBackToTelegramUserId_WhenNeitherNameNorUsernameIsProvided()
    {
        Contact contact = CreateContact(firstName: null, lastName: null, username: null);

        Assert.Equal($"Telegram user {TelegramUserId}", contact.DisplayName);
    }

    [Fact]
    public void DisplayName_FallsBackToTelegramUserId_WhenTelegramSendsOnlyWhitespace()
    {
        Contact contact = CreateContact(firstName: "   ", lastName: "", username: " ");

        Assert.Equal($"Telegram user {TelegramUserId}", contact.DisplayName);
    }

    [Fact]
    public void RefreshTelegramProfile_ClearsAUsernameThatNoLongerExistsUpstream()
    {
        Contact contact = CreateContact(firstName: "Ada", lastName: null, username: "ada");

        contact.RefreshTelegramProfile(firstName: "Ada", lastName: null, username: null, telegramChatId: TelegramChatId);

        Assert.Null(contact.Username);
        Assert.Equal("Ada", contact.DisplayName);
    }

    [Fact]
    public void RefreshTelegramProfile_KeepsTheKnownChatId_WhenTheUpdateCarriesNone()
    {
        Contact contact = CreateContact(firstName: "Ada", lastName: null, username: "ada");

        contact.RefreshTelegramProfile(firstName: "Ada", lastName: null, username: "ada", telegramChatId: 0);

        Assert.Equal(TelegramChatId, contact.TelegramChatId);
    }

    [Fact]
    public void RegisterActivity_MovesLastActivityForward()
    {
        Contact contact = CreateContact();
        DateTimeOffset laterActivityAtUtc = FirstActivityAtUtc.AddMinutes(5);

        contact.RegisterActivity(laterActivityAtUtc);

        Assert.Equal(laterActivityAtUtc, contact.LastActivityAtUtc);
    }

    [Fact]
    public void RegisterActivity_IgnoresAnOlderTimestamp_SoOutOfOrderDeliveriesDoNotReorderTheInbox()
    {
        Contact contact = CreateContact();

        contact.RegisterActivity(FirstActivityAtUtc.AddMinutes(-5));

        Assert.Equal(FirstActivityAtUtc, contact.LastActivityAtUtc);
    }

    [Fact]
    public void CreateFromTelegramSender_RejectsANonPositiveTelegramUserId()
    {
        DomainRuleViolationException thrownException = Assert.Throws<DomainRuleViolationException>(() =>
            Contact.CreateFromTelegramSender(
                telegramUserId: 0,
                telegramChatId: TelegramChatId,
                firstName: "Ada",
                lastName: null,
                username: null,
                firstActivityAtUtc: FirstActivityAtUtc));

        Assert.Contains("Telegram user identifier", thrownException.Message);
    }

    private static Contact CreateContact(
        string? firstName = "Ada",
        string? lastName = "Lovelace",
        string? username = "ada") =>
        Contact.CreateFromTelegramSender(
            TelegramUserId,
            TelegramChatId,
            firstName,
            lastName,
            username,
            FirstActivityAtUtc);
}

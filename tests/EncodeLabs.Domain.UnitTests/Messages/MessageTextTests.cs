using EncodeLabs.Domain.Common;
using EncodeLabs.Domain.Messages;
using Xunit;

namespace EncodeLabs.Domain.UnitTests.Messages;

public sealed class MessageTextTests
{
    [Fact]
    public void Create_TrimsSurroundingWhitespace()
    {
        MessageText messageText = MessageText.Create("  Hello  ");

        Assert.Equal("Hello", messageText.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t")]
    public void Create_RejectsTextWithoutVisibleContent(string? candidateText)
    {
        Assert.Throws<DomainRuleViolationException>(() => MessageText.Create(candidateText));
    }

    [Fact]
    public void Create_AcceptsTextAtTheTelegramLengthLimit()
    {
        string textAtLimit = new('x', MessageText.MaximumLength);

        MessageText messageText = MessageText.Create(textAtLimit);

        Assert.Equal(MessageText.MaximumLength, messageText.Value.Length);
    }

    [Fact]
    public void Create_RejectsTextBeyondTheTelegramLengthLimit()
    {
        string textBeyondLimit = new('x', MessageText.MaximumLength + 1);

        DomainRuleViolationException thrownException =
            Assert.Throws<DomainRuleViolationException>(() => MessageText.Create(textBeyondLimit));

        Assert.Contains(MessageText.MaximumLength.ToString(), thrownException.Message);
    }

    [Fact]
    public void TryCreate_ReportsFailureWithoutThrowing()
    {
        bool wasCreated = MessageText.TryCreate("   ", out MessageText? messageText);

        Assert.False(wasCreated);
        Assert.Null(messageText);
    }
}

using EncodeLabs.Domain.Common;

namespace EncodeLabs.Domain.Messages;

/// <summary>
/// Validated body of a text message. Guarantees a non-blank value that Telegram will accept.
/// </summary>
public sealed record MessageText
{
    /// <summary>Maximum length of a single text message accepted by the Telegram Bot API.</summary>
    public const int MaximumLength = 4096;

    private MessageText(string value) => Value = value;

    public string Value { get; }

    public static MessageText Create(string? candidateText)
    {
        if (string.IsNullOrWhiteSpace(candidateText))
        {
            throw new DomainRuleViolationException("A message must contain at least one non-whitespace character.");
        }

        string trimmedText = candidateText.Trim();
        if (trimmedText.Length > MaximumLength)
        {
            throw new DomainRuleViolationException(
                $"A message may not exceed {MaximumLength} characters but had {trimmedText.Length}.");
        }

        return new MessageText(trimmedText);
    }

    public static bool TryCreate(string? candidateText, out MessageText? messageText)
    {
        try
        {
            messageText = Create(candidateText);
            return true;
        }
        catch (DomainRuleViolationException)
        {
            messageText = null;
            return false;
        }
    }

    public override string ToString() => Value;
}

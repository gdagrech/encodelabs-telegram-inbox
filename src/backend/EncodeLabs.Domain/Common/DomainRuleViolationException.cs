namespace EncodeLabs.Domain.Common;

/// <summary>
/// Raised when an attempt is made to put a domain entity into a state its invariants forbid.
/// </summary>
public sealed class DomainRuleViolationException(string message) : Exception(message);

namespace EncodeLabs.Application.Abstractions.Persistence;

public interface IUnitOfWork
{
    /// <summary>
    /// Commits the tracked changes.
    /// </summary>
    /// <exception cref="DuplicateTelegramMessageException">
    /// Thrown when the commit violates the uniqueness of a Telegram message inside a conversation,
    /// which is how a replayed webhook delivery is detected at the last possible moment.
    /// </exception>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}

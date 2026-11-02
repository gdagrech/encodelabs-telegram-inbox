using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EncodeLabs.Infrastructure.Persistence;

/// <summary>
/// Used only by <c>dotnet ef</c> when generating migrations. Migration scaffolding needs the model,
/// not a reachable server, so this deliberately carries a placeholder connection string instead of
/// reading the real environment — no credentials live in source.
/// </summary>
public sealed class DesignTimeInboxDbContextFactory : IDesignTimeDbContextFactory<InboxDbContext>
{
    private const string PlaceholderConnectionString =
        "Host=localhost;Port=5432;Database=encodelabs_inbox_design_time;Username=postgres";

    public InboxDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<InboxDbContext> optionsBuilder = new();
        optionsBuilder.UseNpgsql(PlaceholderConnectionString);

        return new InboxDbContext(optionsBuilder.Options);
    }
}

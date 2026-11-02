using System.ComponentModel.DataAnnotations;

namespace EncodeLabs.Infrastructure.Persistence;

public sealed class DatabaseOptions
{
    public const string ConfigurationSectionName = "Database";

    /// <summary>Npgsql connection string for the inbox database.</summary>
    [Required(AllowEmptyStrings = false)]
    public string ConnectionString { get; init; } = string.Empty;

    /// <summary>How long start-up keeps waiting for PostgreSQL before giving up and failing loudly.</summary>
    public TimeSpan MigrationTimeout { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>Pause between migration attempts while PostgreSQL is still starting.</summary>
    public TimeSpan MigrationRetryDelay { get; init; } = TimeSpan.FromSeconds(3);
}

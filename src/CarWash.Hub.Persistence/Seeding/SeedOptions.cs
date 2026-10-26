namespace CarWash.Hub.Persistence.Seeding;

/// <summary>
/// Configures what happens to the database at startup.
/// </summary>
/// <remarks>
/// Layer: Persistence. Bound from the <c>Seed</c> configuration section. Phase 1 has no seed data -
/// machines register themselves and the client keys live in configuration. Phase 2 extends this
/// section with the first Super Admin account, which is why it is named for seeding.
/// </remarks>
public sealed class SeedOptions
{
    /// <summary>The configuration section this binds to.</summary>
    public const string SectionName = "Seed";

    /// <summary>
    /// Gets or sets a value indicating whether pending migrations are applied at startup.
    /// </summary>
    /// <remarks>
    /// On in Development and in the Docker Compose stack. Off by default elsewhere: in production a
    /// schema change should be a deliberate, reviewable step (an idempotent script from
    /// <c>dotnet ef migrations script --idempotent</c>) rather than a side effect of a deploy, and
    /// several instances starting at once would otherwise race to migrate.
    /// </remarks>
    public bool ApplyMigrations { get; set; }
}

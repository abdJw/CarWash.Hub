namespace CarWash.Hub.Persistence.Seeding;

/// <summary>
/// Configures what happens to the database at startup.
/// </summary>
/// <remarks>
/// Layer: Persistence. Bound from the <c>Seed</c> configuration section. Machines register
/// themselves and the client keys live in configuration; what is seeded is the reference data the
/// dashboard needs before anyone has configured anything - today, the service catalog.
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

    /// <summary>
    /// Gets or sets a value indicating whether missing reference data is inserted at startup.
    /// </summary>
    /// <remarks>
    /// On by default. Insert-only: a row that exists is never changed, so an owner's edits to a
    /// seeded service survive every restart. Skipped, with a warning, while migrations are pending.
    /// </remarks>
    public bool SeedData { get; set; } = true;

    /// <summary>Gets or sets the first Super Admin's sign-in name.</summary>
    /// <remarks>
    /// The account is created only while no Super Admin exists at all, and only when
    /// <see cref="AdminPassword"/> is set - so it can never overwrite or resurrect anyone. Set the
    /// password from a secret for the first start, then remove it.
    /// </remarks>
    public string? AdminUserName { get; set; }

    /// <summary>Gets or sets the first Super Admin's e-mail address.</summary>
    public string? AdminEmail { get; set; }

    /// <summary>Gets or sets the first Super Admin's display name; the user name when unset.</summary>
    public string? AdminDisplayName { get; set; }

    /// <summary>Gets or sets the first Super Admin's password. A secret: never commit a real one.</summary>
    public string? AdminPassword { get; set; }
}

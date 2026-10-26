namespace CarWash.Hub.Application.Abstractions.Persistence;

/// <summary>
/// Applies migrations and seeds the data the application cannot start without.
/// </summary>
/// <remarks>
/// Layer: Application.
/// <para>
/// Declared here rather than in Persistence so that <c>Program.cs</c> can run startup
/// seeding without referencing a Persistence type - the Api layer is only permitted to
/// touch Persistence inside <c>AddPersistence</c>.
/// </para>
/// </remarks>
public interface IDbInitializer
{
    /// <summary>
    /// Applies any pending migrations and reconciles seed data.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the database is ready.</returns>
    /// <remarks>Idempotent: safe to run on every start, and on several instances at once.</remarks>
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

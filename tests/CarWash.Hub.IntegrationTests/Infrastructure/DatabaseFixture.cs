using CarWash.Hub.Application.Abstractions.Services;
using CarWash.Hub.Persistence.Context;
using CarWash.Hub.Persistence.Interceptors;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Testcontainers.MsSql;

namespace CarWash.Hub.IntegrationTests.Infrastructure;

/// <summary>
/// Supplies a real SQL Server database - and a shared API host over it - for integration tests.
/// </summary>
/// <remarks>
/// <para>
/// Uses the server in <c>CARWASH_TEST_CONNECTION</c> when it is set - CI's SQL Server service, or a
/// developer's local instance. Otherwise it starts a throwaway Testcontainers instance. If neither
/// is available the tests are skipped rather than failed: a missing environment is not a broken
/// build, but a run that skipped them must not be reported as a pass.
/// </para>
/// <para>
/// The database is never reset between runs, so every test works on machines with fresh, unique
/// ids (<see cref="ApiRequests.NewMachineId"/>) and asserts only on those.
/// </para>
/// </remarks>
public sealed class DatabaseFixture : IAsyncLifetime
{
    private const string ConnectionVariable = "CARWASH_TEST_CONNECTION";
    private const string TestDatabaseName = "carwash_hub_test";

    private MsSqlContainer? _container;
    private ApiFactory? _sharedApi;

    /// <summary>Gets the connection string to the test database, or null when unavailable.</summary>
    public string? ConnectionString { get; private set; }

    /// <summary>Gets a value indicating whether a database is available for this run.</summary>
    public bool IsAvailable => ConnectionString is not null;

    /// <summary>Gets why the database is unavailable, for the skip message.</summary>
    public string SkipReason { get; private set; } = string.Empty;

    /// <summary>
    /// Gets an API host with the default test configuration, shared by every test that needs no
    /// overrides - starting a host per test would dominate the run time.
    /// </summary>
    public ApiFactory SharedApi => _sharedApi ??= new ApiFactory(this);

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            ConnectionString = configured;
            await WaitForServerAsync(configured);
        }
        else
        {
            try
            {
                // SQL Server 2022 runs the tests; the migrations pin compatibility level 150, so only
                // SQL valid on the SQL Server 2019 floor is ever emitted.
                _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
                await _container.StartAsync();

                // The container's connection string points at master; the migration creates and uses
                // a dedicated database, so nothing is written to a system database.
                ConnectionString = new SqlConnectionStringBuilder(_container.GetConnectionString())
                {
                    InitialCatalog = TestDatabaseName,
                }.ConnectionString;
            }
            catch (Exception containerFailure)
            {
                _container = null;
                SkipReason =
                    $"No database available: {ConnectionVariable} is not set and Docker could not start a " +
                    $"container ({containerFailure.GetType().Name}).";
                return;
            }
        }

        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    /// <summary>Creates a context against the test database, with the application's interceptor.</summary>
    /// <returns>A new context.</returns>
    public ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new AuditableEntityInterceptor(Substitute.For<ICurrentUserService>(), new SystemClock()))
            .Options;

        return new ApplicationDbContext(options);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_sharedApi is not null)
        {
            await _sharedApi.DisposeAsync();
        }

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    private static async Task WaitForServerAsync(string connectionString)
    {
        // A CI service container accepts TCP connections before it accepts logins.
        var master = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" }.ConnectionString;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var connection = new SqlConnection(master);
                await connection.OpenAsync();
                return;
            }
            catch (SqlException) when (attempt < 30)
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }
    }

    private sealed class SystemClock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}

/// <summary>Shares one database across every test in the collection.</summary>
[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    /// <summary>The collection name.</summary>
    public const string Name = "Database";
}

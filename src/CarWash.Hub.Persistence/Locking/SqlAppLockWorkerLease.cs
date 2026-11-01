using System.Data;
using CarWash.Hub.Application.Abstractions.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace CarWash.Hub.Persistence.Locking;

/// <summary>
/// A worker lease held as a SQL Server application lock.
/// </summary>
/// <remarks>
/// Layer: Persistence.
/// <para>
/// <c>sp_getapplock</c> with a <em>session</em> owner on a connection of the lease's own: the lock
/// lives exactly as long as that connection. Releasing is explicit on dispose, and if the process
/// dies or the connection drops, SQL Server releases it, so a crashed worker never blocks its
/// successor. The connection is not pooled for the same reason - a pooled connection returned to
/// the pool would keep its session, and with it the lock, alive.
/// </para>
/// </remarks>
/// <param name="configuration">Supplies the connection string, read on each acquire.</param>
public sealed class SqlAppLockWorkerLease(IConfiguration configuration) : IWorkerLease
{
    /// <inheritdoc />
    public async Task<IWorkerLeaseHandle?> TryAcquireAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var connectionString = configuration.GetConnectionString(DependencyInjection.ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{DependencyInjection.ConnectionStringName}' is not configured.");

        var connection = new SqlConnection(new SqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText =
                "DECLARE @result int; " +
                "EXEC @result = sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 0; " +
                "SELECT @result;";
            command.Parameters.Add(new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = name });

            // 0 = granted, 1 = granted after waiting; negative = not granted (timeout, deadlock, error).
            var result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
            if (result >= 0)
            {
                return new Handle(name, connection);
            }
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        await connection.DisposeAsync().ConfigureAwait(false);
        return null;
    }

    private sealed class Handle(string name, SqlConnection connection) : IWorkerLeaseHandle
    {
        private bool _disposed;

        public string Name { get; } = name;

        public async Task<bool> IsHeldAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed || connection.State != ConnectionState.Open)
            {
                return false;
            }

            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT APPLOCK_MODE('public', @resource, 'Session');";
                command.Parameters.Add(new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = Name });

                var mode = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
                return string.Equals(mode, "Exclusive", StringComparison.OrdinalIgnoreCase);
            }
            catch (SqlException)
            {
                // A broken connection means the session - and the lock - is gone.
                return false;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            try
            {
                if (connection.State == ConnectionState.Open)
                {
                    await using var command = connection.CreateCommand();
                    command.CommandText = "EXEC sp_releaseapplock @Resource = @resource, @LockOwner = 'Session';";
                    command.Parameters.Add(new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = Name });
                    await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }
            catch (SqlException)
            {
                // Closing the connection below releases it anyway.
            }
            finally
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}

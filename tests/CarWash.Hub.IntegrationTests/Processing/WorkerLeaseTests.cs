using CarWash.Hub.Application.Abstractions.Persistence;
using CarWash.Hub.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace CarWash.Hub.IntegrationTests.Processing;

/// <summary>
/// The worker lease is exclusive across processes - here, across two independent holders - and is
/// free again the moment its holder lets go.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class WorkerLeaseTests(DatabaseFixture database) : IAsyncLifetime
{
    /// <inheritdoc />
    public ValueTask InitializeAsync()
    {
        Assert.SkipUnless(database.IsAvailable, database.SkipReason);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private IWorkerLease Lease => database.SharedApi.Services.GetRequiredService<IWorkerLease>();

    [Fact]
    public async Task A_held_lease_cannot_be_taken_until_it_is_released()
    {
        var name = $"test:{Guid.NewGuid():N}";
        var token = TestContext.Current.CancellationToken;

        var first = await Lease.TryAcquireAsync(name, token);
        first.ShouldNotBeNull();
        (await first.IsHeldAsync(token)).ShouldBeTrue();

        (await Lease.TryAcquireAsync(name, token)).ShouldBeNull();

        await first.DisposeAsync();
        (await first.IsHeldAsync(token)).ShouldBeFalse();

        var second = await Lease.TryAcquireAsync(name, token);
        second.ShouldNotBeNull();
        await second.DisposeAsync();
    }

    [Fact]
    public async Task Different_leases_do_not_block_each_other()
    {
        var token = TestContext.Current.CancellationToken;

        await using var first = await Lease.TryAcquireAsync($"test:{Guid.NewGuid():N}", token);
        await using var second = await Lease.TryAcquireAsync($"test:{Guid.NewGuid():N}", token);

        first.ShouldNotBeNull();
        second.ShouldNotBeNull();
    }
}

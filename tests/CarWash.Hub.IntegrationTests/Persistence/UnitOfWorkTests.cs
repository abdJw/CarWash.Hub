using CarWash.Hub.Application.Abstractions.Persistence;
using CarWash.Hub.Domain.Entities;
using CarWash.Hub.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CarWash.Hub.IntegrationTests.Persistence;

/// <summary>
/// <see cref="IUnitOfWork.ExecuteInTransactionAsync"/> - the replacement for a plain
/// <c>BeginTransactionAsync</c>, which fails under the retrying execution strategy.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class UnitOfWorkTests(DatabaseFixture database) : IAsyncLifetime
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <inheritdoc />
    public ValueTask InitializeAsync()
    {
        Assert.SkipUnless(database.IsAvailable, database.SkipReason);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static GasStation NewStation() => new()
    {
        Id = Guid.CreateVersion7(),
        Code = $"S{Guid.NewGuid():N}"[..20],
        Name = "Test station",
        NameAr = "محطة اختبار",
    };

    [Fact]
    public async Task Several_saves_commit_together()
    {
        var station = NewStation();
        var machine = new Machine { Id = Guid.CreateVersion7(), Code = ApiRequests.NewMachineId() };

        await using (var scope = database.SharedApi.Services.CreateAsyncScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                await unitOfWork.Repository<GasStation, Guid>().AddAsync(station, token);
                await unitOfWork.SaveChangesAsync(token);

                machine.GasStationId = station.Id;
                await unitOfWork.Machines.AddAsync(machine, token);
                await unitOfWork.SaveChangesAsync(token);
            }, Token);
        }

        await using var context = database.CreateContext();
        (await context.Machines.SingleAsync(m => m.Id == machine.Id, Token)).GasStationId.ShouldBe(station.Id);
    }

    [Fact]
    public async Task A_failure_rolls_back_every_save()
    {
        var station = NewStation();

        await using (var scope = database.SharedApi.Services.CreateAsyncScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await Should.ThrowAsync<InvalidOperationException>(() => unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                await unitOfWork.Repository<GasStation, Guid>().AddAsync(station, token);
                await unitOfWork.SaveChangesAsync(token);
                throw new InvalidOperationException("Abandon the transaction.");
            }, Token));
        }

        await using var context = database.CreateContext();
        (await context.GasStations.AnyAsync(s => s.Id == station.Id, Token)).ShouldBeFalse();
    }
}

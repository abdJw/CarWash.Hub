using CarWash.Hub.Infrastructure.Processing;

namespace CarWash.Hub.UnitTests.Processing;

public class ChannelProcessingTriggerTests
{
    [Fact]
    public async Task A_signal_wakes_the_waiting_worker()
    {
        var trigger = new ChannelProcessingTrigger();
        trigger.Signal();

        (await trigger.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)).ShouldBeTrue();
    }

    [Fact]
    public async Task Without_a_signal_the_wait_ends_at_the_timeout() =>
        (await new ChannelProcessingTrigger().WaitAsync(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken))
            .ShouldBeFalse();

    [Fact]
    public async Task Many_signals_while_busy_leave_one_wake_up()
    {
        var trigger = new ChannelProcessingTrigger();
        for (var i = 0; i < 1000; i++)
        {
            trigger.Signal();
        }

        (await trigger.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)).ShouldBeTrue();
        (await trigger.WaitAsync(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken)).ShouldBeFalse();
    }

    [Fact]
    public async Task Stopping_the_host_ends_the_wait_with_cancellation()
    {
        using var stopping = new CancellationTokenSource();
        var wait = new ChannelProcessingTrigger().WaitAsync(TimeSpan.FromMinutes(1), stopping.Token);

        await stopping.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(wait);
    }
}

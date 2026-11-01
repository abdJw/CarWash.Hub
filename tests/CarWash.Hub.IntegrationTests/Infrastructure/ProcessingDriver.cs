using System.Globalization;
using CarWash.Hub.Application.Features.Processing;
using Microsoft.Extensions.DependencyInjection;

namespace CarWash.Hub.IntegrationTests.Infrastructure;

/// <summary>Drives message processing in tests, where the background worker is switched off.</summary>
public static class ProcessingDriver
{
    /// <summary>Runs the processor until no pending message is left, as the worker would.</summary>
    /// <param name="factory">The host.</param>
    /// <returns>The number of messages processed.</returns>
    public static async Task<int> ProcessAllPendingAsync(this ApiFactory factory)
    {
        var total = 0;

        // Bounded, so a processor bug that never drains the queue fails the test instead of hanging it.
        for (var batch = 0; batch < 10_000; batch++)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<IMachineStateProcessor>()
                .ProcessPendingAsync(TestContext.Current.CancellationToken);

            total += result.Messages;
            if (!result.HasMore)
            {
                return total;
            }
        }

        throw new InvalidOperationException("The processing queue did not drain.");
    }

    /// <summary>The <c>data</c> of a controller's service event, as the ESP32 firmware sends it.</summary>
    /// <param name="service">The service code.</param>
    /// <param name="readAt">The machine's reading time.</param>
    /// <param name="machineId">The MAC address the payload repeats.</param>
    /// <returns>The data JSON.</returns>
    public static string ServiceEvent(string service, DateTimeOffset readAt, string machineId = "B0:CB:D8:D0:64:2C") =>
        $$"""{"service":"{{service}}","macAddress":"{{machineId}}","readAt":"{{readAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)}}"}""";
}

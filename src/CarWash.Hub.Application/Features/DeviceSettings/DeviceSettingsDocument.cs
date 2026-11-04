using System.Text;
using System.Text.Json;

namespace CarWash.Hub.Application.Features.DeviceSettings;

/// <summary>One service's settings as a machine is sent them.</summary>
/// <param name="Service">The service code.</param>
/// <param name="PriceQirsh">The price, in qirsh.</param>
/// <param name="Seconds">The run times, one per selector position: three, or one.</param>
/// <remarks>Layer: Application.</remarks>
public sealed record DeviceServiceSettings(string Service, int PriceQirsh, IReadOnlyList<int> Seconds);

/// <summary>
/// Writes the settings document a machine polls for, byte for byte as the firmware expects it.
/// </summary>
/// <remarks>
/// Layer: Application. Pure.
/// <para>
/// The A7670G modem reads the response into a small buffer, so the document is minified, in a fixed
/// field order, ASCII-only and well under the firmware's limits: the whole body under 768 bytes, and
/// the command without its revision under 640. With the largest values the firmware accepts it is
/// about 300 bytes. It is returned as it stands - no envelope around it.
/// </para>
/// <code>{"revision":1,"command":"update_settings","services":{"water":{"price_qirsh":50,"durations_seconds":[180,180,210]},…,"minute_water":{"price_qirsh":25,"duration_seconds":60},…}}</code>
/// </remarks>
public static class DeviceSettingsDocument
{
    /// <summary>The firmware's limit on the whole body, in bytes (exclusive).</summary>
    public const int MaxBytes = 768;

    /// <summary>The firmware's limit on the command without its revision, in bytes (exclusive).</summary>
    public const int MaxCommandBytes = 640;

    /// <summary>The command name the firmware applies.</summary>
    public const string Command = "update_settings";

    /// <summary>Gets the answer for a machine with no desired settings: revision 0.</summary>
    public static ReadOnlyMemory<byte> NoSettings { get; } = "{\"revision\":0}"u8.ToArray();

    private static readonly JsonWriterOptions WriterOptions = new() { Indented = false, SkipValidation = false };

    /// <summary>Writes the <c>services</c> object: every service, in settings order.</summary>
    /// <param name="services">The services' settings.</param>
    /// <returns>The minified JSON object.</returns>
    public static string WriteServices(IReadOnlyList<DeviceServiceSettings> services)
    {
        ArgumentNullException.ThrowIfNull(services);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            writer.WriteStartObject();
            foreach (var service in services)
            {
                writer.WriteStartObject(service.Service);
                writer.WriteNumber("price_qirsh", service.PriceQirsh);
                if (service.Seconds.Count == 1)
                {
                    writer.WriteNumber("duration_seconds", service.Seconds[0]);
                }
                else
                {
                    writer.WriteStartArray("durations_seconds");
                    foreach (var seconds in service.Seconds)
                    {
                        writer.WriteNumberValue(seconds);
                    }

                    writer.WriteEndArray();
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        return Encoding.ASCII.GetString(stream.ToArray());
    }

    /// <summary>Writes the whole document for a revision.</summary>
    /// <param name="revision">The revision, 1 or more.</param>
    /// <param name="servicesJson">The <c>services</c> object, as <see cref="WriteServices"/> wrote it.</param>
    /// <returns>The body, as UTF-8 bytes.</returns>
    public static byte[] Write(long revision, string servicesJson)
    {
        ArgumentException.ThrowIfNullOrEmpty(servicesJson);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteNumber("revision", revision);
            writer.WriteString("command", Command);
            writer.WritePropertyName("services");
            writer.WriteRawValue(servicesJson, skipInputValidation: false);
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    /// <summary>The size of the command without its revision, in bytes - what the firmware checks against <see cref="MaxCommandBytes"/>.</summary>
    /// <param name="servicesJson">The <c>services</c> object.</param>
    /// <returns>The byte count of <c>{"command":"update_settings","services":…}</c>.</returns>
    public static int CommandBytes(string servicesJson) =>
        Encoding.UTF8.GetByteCount($"{{\"command\":\"{Command}\",\"services\":{servicesJson}}}");

    /// <summary>Reads a <c>services</c> object back, for the dashboard.</summary>
    /// <param name="servicesJson">The object, as <see cref="WriteServices"/> wrote it.</param>
    /// <returns>The services' settings, in document order.</returns>
    public static IReadOnlyList<DeviceServiceSettings> Parse(string servicesJson)
    {
        using var document = JsonDocument.Parse(servicesJson);
        var services = new List<DeviceServiceSettings>();

        foreach (var service in document.RootElement.EnumerateObject())
        {
            var price = service.Value.GetProperty("price_qirsh").GetInt32();
            IReadOnlyList<int> seconds = service.Value.TryGetProperty("duration_seconds", out var single)
                ? [single.GetInt32()]
                : [.. service.Value.GetProperty("durations_seconds").EnumerateArray().Select(value => value.GetInt32())];
            services.Add(new DeviceServiceSettings(service.Name, price, seconds));
        }

        return services;
    }
}

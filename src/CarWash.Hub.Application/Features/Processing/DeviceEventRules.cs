namespace CarWash.Hub.Application.Features.Processing;

/// <summary>Checks a device report against the machine it was stored for.</summary>
/// <remarks>Layer: Application. Pure.</remarks>
public static class DeviceEventRules
{
    /// <summary>
    /// Fails a result whose <c>device_mac</c> names a different machine than its <c>machineId</c>.
    /// </summary>
    /// <param name="result">The parsed message.</param>
    /// <param name="machineCode">The machine the message was stored for.</param>
    /// <returns>The result unchanged, or a failure naming both identities.</returns>
    /// <remarks>
    /// The firmware sends its own MAC in both places, so a mismatch is a firmware bug or a forged
    /// message - neither of which may write a sale or change the machine's state.
    /// </remarks>
    public static MessageParseResult CheckIdentity(MessageParseResult result, string machineCode)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Device?.DeviceMac is not { } mac || MacMatches(mac, machineCode))
        {
            return result;
        }

        return MessageParseResult.Failed($"device_mac '{Clip(mac)}' does not match machineId '{Clip(machineCode)}'.");
    }

    /// <summary>
    /// Whether two machine identities are the same address, ignoring case and separators - so
    /// <c>aa-bb-cc-dd-ee-ff</c> matches <c>AA:BB:CC:DD:EE:FF</c>.
    /// </summary>
    /// <param name="deviceMac">The address in the message.</param>
    /// <param name="machineCode">The machine's id.</param>
    /// <returns><see langword="true"/> if they match.</returns>
    public static bool MacMatches(string deviceMac, string machineCode)
    {
        var left = Normalise(deviceMac);
        return left.Length > 0 && left == Normalise(machineCode);
    }

    private static string Normalise(string value) =>
        string.Concat(value.Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant));

    private static string Clip(string value) => value.Length <= 40 ? value : string.Concat(value.AsSpan(0, 40), "…");
}

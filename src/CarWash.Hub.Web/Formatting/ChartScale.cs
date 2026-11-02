namespace CarWash.Hub.Web.Formatting;

/// <summary>The value axis of the dashboard's column charts.</summary>
/// <remarks>Layer: Web.</remarks>
public static class ChartScale
{
    private static readonly double[] Steps = [2, 4, 6, 8, 10];

    /// <summary>
    /// Returns a clean, even ceiling for the axis - 2, 4, 6, 8 or 10 times a power of ten - so both
    /// tick labels, the top and the half-way line, are round numbers.
    /// </summary>
    /// <param name="max">The largest value drawn.</param>
    /// <returns>The axis top; 2 when nothing is drawn.</returns>
    public static double NiceCeiling(double max)
    {
        if (max <= 0)
        {
            return 2;
        }

        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(max)));
        return Steps.First(step => step * magnitude >= max) * magnitude;
    }
}

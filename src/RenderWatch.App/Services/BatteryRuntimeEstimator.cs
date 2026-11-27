namespace RenderWatch.App.Services;

internal sealed class BatteryRuntimeEstimator
{
    private const double TimeConstantMs = 30_000;
    private const long ResetGapMs = 90_000;
    private long? _lastDischargeTime;
    private double? _averageWatts;
    private double? _averageWindowsSeconds;

    internal BatteryReading? Update(BatteryReading? reading, long now)
    {
        if (reading is not { Mode: BatteryMode.OnBattery })
        {
            _lastDischargeTime = null;
            _averageWatts = null;
            _averageWindowsSeconds = null;
            return reading;
        }

        long elapsed = _lastDischargeTime is long last ? now - last : ResetGapMs;
        _lastDischargeTime = now;
        double weight = elapsed < 0 || elapsed >= ResetGapMs ? 1 : 1 - Math.Exp(-elapsed / TimeConstantMs);
        _averageWatts = UpdateAverage(_averageWatts, reading.Watts, weight);
        _averageWindowsSeconds = UpdateAverage(_averageWindowsSeconds, reading.Remaining?.TotalSeconds, weight);

        double? seconds = _averageWindowsSeconds;
        if (reading.EnergyWh is > 0 && _averageWatts is > 0)
        {
            double estimate = reading.EnergyWh.Value / _averageWatts.Value * 3_600;
            if (double.IsFinite(estimate) && estimate < TimeSpan.MaxValue.TotalSeconds) seconds = estimate;
        }

        var remaining = seconds is > 0 ? TimeSpan.FromSeconds(seconds.Value) : (TimeSpan?)null;
        return reading with { Remaining = remaining };
    }

    private static double? UpdateAverage(double? previous, double? next, double weight) =>
        next is > 0 && double.IsFinite(next.Value)
            ? previous is double value ? value + (next.Value - value) * weight : next
            : null;
}

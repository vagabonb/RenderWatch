using RenderWatch.Core.Models;

namespace RenderWatch.Tests;

public class FanScaleTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(100, 3.3333333333)]
    [InlineData(1424, 47.4666666667)]
    [InlineData(2000, 66.6666666667)]
    [InlineData(2200, 73.3333333333)]
    [InlineData(2201, 73.3666666667)]
    [InlineData(2600, 86.6666666667)]
    [InlineData(2999, 99.9666666667)]
    [InlineData(3000, 100)]
    [InlineData(3001, 100)]
    public void DesktopScale_IsLinearAndReachesTheEdgeAt3000(double rpm, double expected)
    {
        Assert.Equal(expected, new FanMetric { Role = "CPU", Rpm = rpm }.CalculatePercent(), 8);
    }

    [Theory]
    [InlineData("CPU")]
    [InlineData("GPU")]
    [InlineData("Корпус")]
    public void IncreasingRpm_NeverMakesTheBarGoBackwards(string role)
    {
        var metric = new FanMetric { Role = role };
        double previous = 0;
        for (int rpm = 0; rpm <= 12000; rpm++)
        {
            metric.Rpm = rpm;
            double current = metric.CalculatePercent();
            Assert.True(current >= previous, $"{role}: scale fell from {previous} to {current} at {rpm} RPM");
            Assert.InRange(current, 0, 100);
            previous = current;
        }
    }

    [Theory]
    [InlineData(0, 100, 0)]
    [InlineData(1424, 100, 47.4666666667)]
    [InlineData(2600, 100, 86.6666666667)]
    [InlineData(3000, 25, 100)]
    public void TachometerHasPriorityOverTheControlSignal(double rpm, double control, double expected)
    {
        Assert.Equal(expected, new FanMetric { Rpm = rpm, Percent = control }.CalculatePercent(), 8);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData(-10, 0)]
    [InlineData(0, 0)]
    [InlineData(65, 65)]
    [InlineData(150, 100)]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 0)]
    public void WithoutTachometer_UsesOnlyAValidControlSignal(double? control, double expected)
    {
        Assert.Equal(expected, new FanMetric { Percent = control }.CalculatePercent());
    }

    [Theory]
    [InlineData("laptop-cpu-fan", "CPU", "CPU Fan")]
    [InlineData("laptop-gpu-fan", "GPU", "GPU Fan")]
    [InlineData("pump", "Помпа", "Fan #6")]
    [InlineData("pump", "Pump", "Fan #6")]
    [InlineData("pump", "Корпус", "AIO_PUMP")]
    [InlineData("pump", "Корпус", "Помпа СЖО")]
    public void LaptopBlowersAndPumps_UseAStable6000Reference(string id, string role, string name)
    {
        var metric = new FanMetric { Id = id, Role = role, Name = name };
        Assert.Equal(6000, metric.ScaleMaxRpm);
        foreach (int rpm in new[] { 0, 2000, 2200, 2201, 3500, 3501, 4500, 5999, 6000 })
        {
            metric.Rpm = rpm;
            Assert.Equal(rpm / 6000.0 * 100, metric.CalculatePercent(), 8);
        }
    }

    [Theory]
    [InlineData(1500)]
    [InlineData(3000)]
    [InlineData(6000)]
    [InlineData(12000)]
    public void ExplicitReference_SurvivesNewSamplesAndReverseSweep(double maximum)
    {
        for (int rpm = 12000; rpm >= 0; rpm--)
        {
            var metric = new FanMetric { Rpm = rpm, ReferenceMaxRpm = maximum, Percent = 100 };
            Assert.Equal(maximum, metric.ScaleMaxRpm);
            Assert.Equal(Math.Clamp(rpm / maximum, 0, 1) * 100, metric.CalculatePercent(), 8);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidReference_FallsBackToTheFixedDefault(double maximum)
    {
        var metric = new FanMetric { Rpm = 2600, ReferenceMaxRpm = maximum };
        Assert.Equal(3000, metric.ScaleMaxRpm);
        Assert.Equal(2600.0 / 3000 * 100, metric.CalculatePercent(), 8);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidTachometer_NeverFillsTheBar(double rpm)
    {
        Assert.Equal(0, new FanMetric { Rpm = rpm, Percent = 100 }.CalculatePercent());
    }
}

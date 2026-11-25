using System.Collections.ObjectModel;

namespace RenderWatch.App.ViewModels;

public sealed class TransferHistory : ViewModelBase
{
    public const int Capacity = 120;
    internal const double InitialScale = 256 * 1024;

    public ObservableCollection<double> Points { get; } = new();

    private double _scale = InitialScale;
    public double Scale
    {
        get => _scale;
        private set => SetField(ref _scale, value);
    }

    internal void Add(double? bytesPerSecond) => Scale = Append(Points, bytesPerSecond, Scale);

    internal static double Append(ObservableCollection<double> points, double? value, double scale)
    {
        if (value is not double rate || !double.IsFinite(rate) || rate < 0)
            return scale;

        points.Add(rate);
        if (points.Count > Capacity) points.RemoveAt(0);
        return ExpandScale(scale, rate);
    }

    internal static double ExpandScale(double scale, double rate)
    {
        while (scale < rate && scale <= double.MaxValue / 2) scale *= 2;
        return scale;
    }
}

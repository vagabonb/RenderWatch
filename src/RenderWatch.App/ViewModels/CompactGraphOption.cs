using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace RenderWatch.App.ViewModels;

public sealed class CompactGraphOption : ViewModelBase
{
    private readonly Action _configurationChanged;
    private bool _enabled;
    private string _reading = "—";
    private double _scale;

    public CompactGraphOption(string id, string title, string unit, ObservableCollection<double> points,
        double scale, Action configurationChanged)
    {
        Id = id;
        Title = title;
        Unit = unit;
        IsTransferRate = unit == "Б/с";
        Points = points;
        _scale = scale;
        _configurationChanged = configurationChanged;
        CollectionChangedEventManager.AddHandler(points, (_, _) => UpdateReading());
        UpdateReading();
    }

    public string Id { get; }
    public string Title { get; }
    public string Unit { get; }
    public bool IsTransferRate { get; }
    public ObservableCollection<double> Points { get; }
    public double Scale { get => _scale; private set => SetField(ref _scale, value); }
    public string Reading { get => _reading; private set => SetField(ref _reading, value); }

    public bool IsEnabled
    {
        get => _enabled;
        set
        {
            if (SetField(ref _enabled, value)) _configurationChanged();
        }
    }

    internal void SetInitialVisibility(bool enabled) => SetField(ref _enabled, enabled, nameof(IsEnabled));

    private void UpdateReading()
    {
        if (Points.Count == 0 || !double.IsFinite(Points[^1]))
        {
            Reading = "—";
            return;
        }

        double value = Points[^1];
        if (!IsTransferRate)
        {
            Reading = $"{value:F0}{Unit}";
            return;
        }

        Scale = TransferHistory.ExpandScale(Scale, value);
        Reading = value switch
        {
            >= 1073741824 => $"{value / 1073741824:0.#} ГБ/с",
            >= 1048576 => $"{value / 1048576:0.#} МБ/с",
            _ => $"{value / 1024:0.#} КБ/с"
        };
    }
}

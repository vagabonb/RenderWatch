using System.Globalization;
using System.Windows.Input;
using System.Windows.Media;
using RenderWatch.Core.Models;

namespace RenderWatch.App.ViewModels;

public sealed class TaskbarMetricAppearance : ViewModelBase
{
    private TaskbarMetricSettings _settings = new();
    private readonly Action _save;

    public TaskbarMetricAppearance(string key, string name, Action save)
    {
        Key = key;
        Name = name;
        _save = save;
        ResetColorsCommand = new RelayCommand(() =>
        {
            LabelColor = ValueColor = TemperatureColor = string.Empty;
        });
    }

    public string Key { get; }
    public string Name { get; }
    public bool HasTemperature => Key is "CPU" or "GPU";
    public ICommand ResetColorsCommand { get; }

    public bool UseText
    {
        get => _settings.UseText;
        set
        {
            if (_settings.UseText == value) return;
            _settings.UseText = value;
            OnPropertyChanged();
            _save();
        }
    }

    public string LabelColor
    {
        get => _settings.LabelColor;
        set => UpdateColor(value, nameof(LabelColor), nameof(LabelBrush), _settings.LabelColor,
            color => _settings.LabelColor = color);
    }

    public string ValueColor
    {
        get => _settings.ValueColor;
        set
        {
            if (UpdateColor(value, nameof(ValueColor), nameof(ValueBrush), _settings.ValueColor,
                color => _settings.ValueColor = color))
                OnPropertyChanged(nameof(TemperatureBrush));
        }
    }

    public string TemperatureColor
    {
        get => _settings.TemperatureColor;
        set => UpdateColor(value, nameof(TemperatureColor), nameof(TemperatureBrush), _settings.TemperatureColor,
            color => _settings.TemperatureColor = color);
    }

    public Brush? LabelBrush => ToBrush(LabelColor);
    public Brush? ValueBrush => ToBrush(ValueColor);
    public Brush? TemperatureBrush => ToBrush(TemperatureColor) ?? ValueBrush;

    internal void Initialize(TaskbarMetricSettings settings)
    {
        _settings = settings;
        _settings.LabelColor = NormalizeColor(settings.LabelColor) ?? string.Empty;
        _settings.ValueColor = NormalizeColor(settings.ValueColor) ?? string.Empty;
        _settings.TemperatureColor = NormalizeColor(settings.TemperatureColor) ?? string.Empty;
        OnPropertyChanged(string.Empty);
    }

    private bool UpdateColor(string text, string property, string brushProperty, string current, Action<string> apply)
    {
        string normalized = NormalizeColor(text) ?? throw new FormatException("Введите цвет в формате #RRGGBB.");
        if (normalized == current) return false;
        apply(normalized);
        OnPropertyChanged(property);
        OnPropertyChanged(brushProperty);
        _save();
        return true;
    }

    private static string? NormalizeColor(string? text)
    {
        text = text?.Trim() ?? string.Empty;
        if (text.Length == 0) return string.Empty;
        var hex = text.StartsWith('#') ? text[1..] : text;
        return hex.Length == 6 && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _)
            ? "#" + hex.ToUpperInvariant()
            : null;
    }

    private static Brush? ToBrush(string hex)
    {
        if (hex.Length == 0) return null;
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}

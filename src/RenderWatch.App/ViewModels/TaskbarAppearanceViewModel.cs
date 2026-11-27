using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using RenderWatch.Core.Models;

namespace RenderWatch.App.ViewModels;

public sealed class TaskbarAppearanceViewModel : ViewModelBase
{
    private TaskbarAppearanceSettings _settings = new();
    private readonly Action _save;
    private TaskbarMetricAppearance? _selectedMetric;

    public TaskbarAppearanceViewModel(Action save)
    {
        _save = save;
        Options = new ObservableCollection<TaskbarMetricAppearance>(
        [
            new("CPU", "CPU", save), new("GPU", "GPU", save),
            new("RAM", "RAM", save), new("VRAM", "VRAM", save),
            new("DISK", "Диски", save), new("NET", "Сеть", save), new("UPTIME", "Время работы", save),
            new("BAT", "Осталось от батареи", save)
        ]);
        ByKey = Options.ToDictionary(option => option.Key, StringComparer.Ordinal);
        SelectedMetric = Options[0];
        MoveLeftCommand = new RelayCommand(() => Move(-1), () => CanMove(-1));
        MoveRightCommand = new RelayCommand(() => Move(1), () => CanMove(1));
    }

    public ObservableCollection<TaskbarMetricAppearance> Options { get; }
    public IReadOnlyDictionary<string, TaskbarMetricAppearance> ByKey { get; }
    public string Order => string.Join(",", Options.Select(option => option.Key));
    public ICommand MoveLeftCommand { get; }
    public ICommand MoveRightCommand { get; }

    public TaskbarMetricAppearance? SelectedMetric
    {
        get => _selectedMetric;
        set
        {
            if (SetField(ref _selectedMetric, value)) CommandManager.InvalidateRequerySuggested();
        }
    }

    public int BlockGap
    {
        get => _settings.BlockGap;
        set => ChangeSpacing(value, 20, BlockGap, gap => _settings.BlockGap = gap, nameof(BlockGap), nameof(BlockPadding));
    }

    public int LabelGap
    {
        get => _settings.LabelGap;
        set => ChangeSpacing(value, 16, LabelGap, gap => _settings.LabelGap = gap, nameof(LabelGap), nameof(LabelPadding));
    }

    public int ValueGap
    {
        get => _settings.ValueGap;
        set => ChangeSpacing(value, 16, ValueGap, gap => _settings.ValueGap = gap, nameof(ValueGap), nameof(ValuePadding));
    }

    public int SidePadding
    {
        get => _settings.SidePadding;
        set => ChangeSpacing(value, 20, SidePadding, gap => _settings.SidePadding = gap, nameof(SidePadding), nameof(OuterPadding));
    }

    public Thickness BlockPadding => new(BlockGap / 2.0, 0, BlockGap / 2.0, 0);
    public Thickness LabelPadding => new(0, 0, LabelGap, 0);
    public Thickness ValuePadding => new(ValueGap, 0, 0, 0);
    public Thickness OuterPadding => new(SidePadding, 0, SidePadding, 0);

    internal void Initialize(TaskbarAppearanceSettings settings, string legacyOrder)
    {
        _settings = settings;
        _settings.BlockGap = Math.Clamp(settings.BlockGap, 0, 20);
        _settings.LabelGap = Math.Clamp(settings.LabelGap, 0, 16);
        _settings.ValueGap = Math.Clamp(settings.ValueGap, 0, 16);
        _settings.SidePadding = Math.Clamp(settings.SidePadding, 0, 20);
        _settings.Metrics ??= new();
        var order = (settings.Order ?? legacyOrder).Split(',')
            .Where(ByKey.ContainsKey).Concat(ByKey.Keys).Distinct().ToArray();
        for (int index = 0; index < order.Length; index++)
        {
            TaskbarMetricAppearance option = ByKey[order[index]];
            Options.Move(Options.IndexOf(option), index);
            if (!_settings.Metrics.TryGetValue(option.Key, out var item) || item == null)
                _settings.Metrics[option.Key] = item = new TaskbarMetricSettings();
            option.Initialize(item);
        }
        _settings.Order = Order;
        OnPropertyChanged(string.Empty);
    }

    private bool CanMove(int offset)
    {
        int index = SelectedMetric is null ? -1 : Options.IndexOf(SelectedMetric);
        return index >= 0 && index + offset >= 0 && index + offset < Options.Count;
    }

    private void Move(int offset)
    {
        if (!CanMove(offset)) return;
        int index = Options.IndexOf(SelectedMetric!);
        Options.Move(index, index + offset);
        _settings.Order = Order;
        OnPropertyChanged(nameof(Order));
        CommandManager.InvalidateRequerySuggested();
        _save();
    }

    private void ChangeSpacing(int value, int maximum, int current, Action<int> apply, string property, string padding)
    {
        value = Math.Clamp(value, 0, maximum);
        if (value == current) return;
        apply(value);
        OnPropertyChanged(property);
        OnPropertyChanged(padding);
        _save();
    }
}

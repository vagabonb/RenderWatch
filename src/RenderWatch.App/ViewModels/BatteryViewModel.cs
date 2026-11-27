using System.Windows.Media;
using RenderWatch.App.Controls;
using RenderWatch.App.Services;

namespace RenderWatch.App.ViewModels;

public sealed class BatteryViewModel : ViewModelBase
{
    internal BatteryReading? Reading { get; private set; }
    public bool IsPresent => Reading != null;
    public bool IsOnBattery => Reading?.Mode == BatteryMode.OnBattery;
    public int? ChargePercent => Reading?.ChargePercent;
    public double RingFill => ChargePercent ?? 0;
    public string ChargeText => ChargePercent is int charge ? $"{charge}%" : "—";
    public SolidColorBrush? AccentBrush => ChargePercent is int charge ? TelemetryPalette.ForBattery(charge) : null;
    public string StateText => Reading?.Mode switch
    {
        BatteryMode.OnBattery => "От батареи",
        BatteryMode.Charging => "Заряжается",
        BatteryMode.PluggedIn => "От сети",
        _ => "—"
    };
    public string PowerCaption => Reading?.Mode switch
    {
        BatteryMode.OnBattery => "Разряд",
        BatteryMode.Charging => "Зарядка",
        _ => "Мощность"
    };
    public string PowerText => Reading?.Watts is double watts ? $"{watts:0.0} Вт" : "—";
    public string RemainingText => Reading?.Remaining is TimeSpan remaining ? FormatRemaining(remaining) : "—";
    public string TaskbarRemainingText
    {
        get
        {
            if (Reading?.Remaining is not TimeSpan remaining) return "—";
            int hours = (int)remaining.TotalHours;
            if (hours > 0) return $"{hours}ч {remaining.Minutes:00}м";
            return remaining.Minutes > 0 ? $"{remaining.Minutes}м" : "<1м";
        }
    }

    internal void Update(BatteryReading? reading)
    {
        if (Reading == reading) return;
        Reading = reading;
        OnPropertyChanged(string.Empty);
    }

    private static string FormatRemaining(TimeSpan remaining)
    {
        int hours = (int)remaining.TotalHours;
        if (hours > 0) return $"≈ {hours} ч {remaining.Minutes} мин";
        if (remaining.Minutes > 0) return $"≈ {remaining.Minutes} мин";
        return "< 1 мин";
    }
}

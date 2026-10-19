using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using RenderWatch.Core.Hardware;
using RenderWatch.Core.Models;

namespace RenderWatch.App.ViewModels;

public class ConfigurationViewModel : ViewModelBase, IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus; // 0 = Offline (Battery), 1 = Online (AC), 255 = Unknown
        public byte BatteryFlag;  // 1 = High, 2 = Low, 4 = Critical, 8 = Charging, 128 = No battery
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

    private SystemConfigurationInfo _config = new();
    private bool _isLoading = true;
    private string _copyFeedbackMessage = string.Empty;
    private bool _isCopyFeedbackVisible;
    private readonly DispatcherTimer _powerTimer;
    private readonly Microsoft.Win32.PowerModeChangedEventHandler _powerModeChangedHandler;

    public SystemConfigurationInfo Config
    {
        get => _config;
        set
        {
            if (SetField(ref _config, value))
            {
                OnPropertyChanged(nameof(PowerSource));
            }
        }
    }

    public string PowerSource => Config.PowerSource;

    public bool IsLoading
    {
        get => _isLoading;
        set => SetField(ref _isLoading, value);
    }

    public string CopyFeedbackMessage
    {
        get => _copyFeedbackMessage;
        set => SetField(ref _copyFeedbackMessage, value);
    }

    public bool IsCopyFeedbackVisible
    {
        get => _isCopyFeedbackVisible;
        set => SetField(ref _isCopyFeedbackVisible, value);
    }

    public ICommand RefreshCommand { get; }
    public ICommand CopySpecsCommand { get; }

    public ConfigurationViewModel()
    {
        RefreshCommand = new RelayCommand(_ => Refresh());
        CopySpecsCommand = new RelayCommand(_ => CopySpecs());

        _powerModeChangedHandler = (s, e) =>
        {
            Application.Current?.Dispatcher.Invoke(CheckLivePowerStatus);
        };

        try
        {
            Microsoft.Win32.SystemEvents.PowerModeChanged += _powerModeChangedHandler;
        }
        catch { }

        _powerTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _powerTimer.Tick += (s, e) => CheckLivePowerStatus();
        _powerTimer.Start();

        _ = LoadInitialConfigAsync();
    }

    public void CheckLivePowerStatus()
    {
        try
        {
            if (GetSystemPowerStatus(out SYSTEM_POWER_STATUS status))
            {
                string source;
                if (status.ACLineStatus == 0)
                {
                    // Disconnected from AC - running on battery
                    string batt = (status.BatteryLifePercent <= 100) ? $" ({status.BatteryLifePercent}%)" : string.Empty;
                    source = $"Автономно (от аккумулятора){batt}";
                }
                else if (status.ACLineStatus == 1)
                {
                    // Plugged into AC power
                    bool isCharging = (status.BatteryFlag & 8) != 0;
                    string charge = isCharging ? " [Зарядка]" : string.Empty;
                    source = $"Питание от электросети{charge}";
                }
                else
                {
                    source = "Питание от электросети";
                }

                if (Config.PowerSource != source)
                {
                    Config.PowerSource = source;
                    OnPropertyChanged(nameof(PowerSource));
                }
            }
        }
        catch { }
    }

    private async Task LoadInitialConfigAsync()
    {
        IsLoading = true;
        try
        {
            var config = await SystemInfoService.GetConfigurationAsync();
            Application.Current?.Dispatcher.Invoke(() =>
            {
                Config = config;
                IsLoading = false;
            });
        }
        catch
        {
            IsLoading = false;
        }
    }

    public async void Refresh()
    {
        IsLoading = true;
        try
        {
            var config = await SystemInfoService.GetConfigurationAsync(forceRefresh: true);
            Application.Current?.Dispatcher.Invoke(() =>
            {
                Config = config;
                IsLoading = false;
            });
        }
        catch
        {
            IsLoading = false;
        }
    }

    private async void CopySpecs()
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("==================================================");
            sb.AppendLine("RENDER WATCH — КОНФИГУРАЦИЯ СИСТЕМЫ");
            sb.AppendLine("==================================================");
            sb.AppendLine();
            sb.AppendLine("ПРОЦЕССОР (CPU):");
            sb.AppendLine($"  Модель: {Config.CpuName}");
            sb.AppendLine($"  Конфигурация: {Config.CpuCoresThreads}");
            sb.AppendLine($"  Базовая частота: {Config.CpuFrequency}");
            sb.AppendLine($"  Максимальная частота: {Config.CpuMaxFrequency}");
            sb.AppendLine($"  Сокет: {Config.CpuSocket}");
            sb.AppendLine($"  Кэш: {Config.CpuCache}");
            sb.AppendLine($"  Архитектура: {Config.CpuArchitecture}");
            sb.AppendLine();
            sb.AppendLine("ОПЕРАТИВНАЯ ПАМЯТЬ (RAM):");
            sb.AppendLine($"  Объем: {Config.RamTotalCapacity} {Config.RamType}");
            sb.AppendLine($"  Режим работы каналов: {Config.RamChannelMode}");
            sb.AppendLine($"  Частота: {Config.RamSpeed}");
            sb.AppendLine($"  Слоты: {Config.RamSlotsUsed}");
            if (Config.RamModules.Count > 0)
            {
                sb.AppendLine("  Установленные модули:");
                foreach (var mod in Config.RamModules)
                {
                    sb.AppendLine($"    • {mod.Slot}: {mod.Capacity} {mod.Type}-{mod.Speed} ({mod.Manufacturer} {mod.PartNumber}) [{mod.ChannelBank}]");
                }
            }
            sb.AppendLine();
            sb.AppendLine("МАТЕРИНСКАЯ ПЛАТА (MOTHERBOARD):");
            sb.AppendLine($"  Производитель: {Config.MotherboardManufacturer}");
            sb.AppendLine($"  Модель: {Config.MotherboardProduct} (v{Config.MotherboardVersion})");
            sb.AppendLine($"  BIOS: {Config.BiosVersion} от {Config.BiosDate} ({Config.BiosVendor})");
            sb.AppendLine();
            sb.AppendLine("ВИДЕОКАРТА (GPU):");
            sb.AppendLine($"  Основная GPU: {Config.PrimaryGpuName} ({Config.PrimaryGpuVram})");
            sb.AppendLine($"  Версия драйвера: {Config.PrimaryGpuDriver}");
            if (Config.HasSecondaryGpu)
            {
                sb.AppendLine($"  Встроенная графика: {Config.SecondaryGpuName} ({Config.SecondaryGpuVram})");
            }
            sb.AppendLine();
            sb.AppendLine("НАКОПИТЕЛИ И ДИСКИ (STORAGE):");
            sb.AppendLine($"  Суммарная емкость: {Config.TotalStorageCapacity}");
            if (!string.IsNullOrWhiteSpace(Config.PhysicalDisksSummary))
            {
                sb.AppendLine($"  Физические накопители: {Config.PhysicalDisksSummary}");
            }
            if (Config.Disks.Count > 0)
            {
                sb.AppendLine("  Логические диски:");
                foreach (var d in Config.Disks)
                {
                    sb.AppendLine($"    • {d.Name}: {d.FreeSpace} из {d.TotalSpace} (Занято: {d.UsedSpace}, ФС: {d.FileSystem})");
                }
            }
            sb.AppendLine();
            sb.AppendLine("ПОДСИСТЕМА ПИТАНИЯ:");
            sb.AppendLine($"  Тип устройства: {Config.SystemType}");
            sb.AppendLine($"  Источник питания: {Config.PowerSource}");
            sb.AppendLine($"  Рекомендуемый объем БП: {Config.RecommendedPsu}");
            sb.AppendLine("==================================================");

            Clipboard.SetText(sb.ToString());

            CopyFeedbackMessage = "✓ Конфигурация скопирована в буфер обмена!";
            IsCopyFeedbackVisible = true;
            await Task.Delay(3000);
            IsCopyFeedbackVisible = false;
        }
        catch
        {
            CopyFeedbackMessage = "Не удалось скопировать в буфер";
            IsCopyFeedbackVisible = true;
            await Task.Delay(2000);
            IsCopyFeedbackVisible = false;
        }
    }

    public void Dispose()
    {
        _powerTimer.Stop();
        try
        {
            Microsoft.Win32.SystemEvents.PowerModeChanged -= _powerModeChangedHandler;
        }
        catch { }
    }
}


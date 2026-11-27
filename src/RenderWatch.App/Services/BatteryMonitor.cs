using System.Runtime.InteropServices;

namespace RenderWatch.App.Services;

internal enum BatteryMode { Unknown, OnBattery, Charging, PluggedIn }

internal sealed record BatteryReading(int? ChargePercent, BatteryMode Mode, double? Watts, TimeSpan? Remaining, double? EnergyWh = null);

internal sealed class BatteryMonitor
{
    private const int SystemBatteryState = 5;
    private const byte Unknown = byte.MaxValue;
    private const int NoBattery = 128;
    private const int Charging = 8;
    private readonly BatteryRuntimeEstimator _runtime = new();

    internal BatteryReading? Read()
    {
        long now = Environment.TickCount64;
        PowerStatus? power = GetSystemPowerStatus(out var status) ? status : null;
        if (power is { } known && IsWithoutBattery(known)) return _runtime.Update(null, now);
        NativeBatteryState? battery = CallNtPowerInformation(SystemBatteryState, 0, 0, out var state,
            (uint)Marshal.SizeOf<NativeBatteryState>()) == 0 && state.BatteryPresent != 0 ? state : null;
        return _runtime.Update(Interpret(power, battery), now);
    }

    internal static BatteryReading? Interpret(PowerStatus? power, NativeBatteryState? battery)
    {
        if (power is { } status && IsWithoutBattery(status)) return null;
        if (battery is not { BatteryPresent: not 0 }) battery = null;
        int? charge = power?.BatteryLifePercent is <= 100 and byte percent ? percent : null;
        if (battery == null && charge == null && (power == null || power.Value.BatteryFlag == Unknown)) return null;

        bool charging = battery?.Charging > 0 ||
            (power is { BatteryFlag: not Unknown } flags && (flags.BatteryFlag & Charging) != 0);
        int rate = battery?.Rate ?? int.MinValue;
        var mode = charging ? BatteryMode.Charging
            : power?.ACLineStatus == 1 || (power?.ACLineStatus != 0 && battery?.AcOnLine > 0) ? BatteryMode.PluggedIn
            : power?.ACLineStatus == 0 || battery?.Discharging > 0 || (rate < 0 && rate != int.MinValue) ? BatteryMode.OnBattery
            : BatteryMode.Unknown;
        double? watts = mode switch
        {
            BatteryMode.OnBattery when rate < 0 && rate != int.MinValue => -(double)rate / 1000,
            BatteryMode.Charging when rate > 0 => rate / 1000.0,
            _ => null
        };
        TimeSpan? remaining = null;
        if (mode == BatteryMode.OnBattery)
        {
            uint seconds = power?.BatteryLifeTime ?? uint.MaxValue;
            if (seconds is 0 or uint.MaxValue) seconds = battery?.EstimatedTime ?? uint.MaxValue;
            if (seconds is > 0 and < uint.MaxValue) remaining = TimeSpan.FromSeconds(seconds);
        }
        double? energyWh = battery?.RemainingCapacity is > 0 and < uint.MaxValue and uint capacity
            ? capacity / 1000.0 : null;
        return new BatteryReading(charge, mode, watts, remaining, energyWh);
    }

    private static bool IsWithoutBattery(PowerStatus power) =>
        power.BatteryFlag != Unknown && (power.BatteryFlag & NoBattery) != 0;

    [StructLayout(LayoutKind.Sequential)]
    internal struct PowerStatus
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public uint BatteryLifeTime, BatteryFullLifeTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeBatteryState
    {
        public byte AcOnLine, BatteryPresent, Charging, Discharging;
        public byte Reserved1, Reserved2, Reserved3, Tag;
        public uint MaxCapacity, RemainingCapacity;
        public int Rate;
        public uint EstimatedTime, DefaultAlert1, DefaultAlert2;
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetSystemPowerStatus(out PowerStatus status);

    [DllImport("powrprof.dll")]
    private static extern uint CallNtPowerInformation(int informationLevel, nint input, uint inputLength,
        out NativeBatteryState output, uint outputLength);
}

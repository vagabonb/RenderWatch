using RenderWatch.Core.Models;

namespace RenderWatch.Core.Hardware;

public interface IHardwareMonitor : IDisposable
{
    string ProviderName { get; }
    bool IsAvailable { get; }
    Task InitializeAsync();
    Task<HardwareMetrics> GetMetricsAsync();
}
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RenderWatch.App.Utils;

public static class MemoryOptimizer
{
    [DllImport("psapi.dll")]
    private static extern int EmptyWorkingSet(IntPtr hwProc);

    public static void Trim()
    {
        try
        {
            GC.Collect(2, GCCollectionMode.Aggressive, true, true);
            GC.WaitForPendingFinalizers();
            EmptyWorkingSet(Process.GetCurrentProcess().Handle);
        }
        catch { }
    }
}
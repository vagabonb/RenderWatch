using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RenderWatch.Core.Tracking;

public class HangDetector
{
    [DllImport("user32.dll")]
    private static extern bool IsHungAppWindow(IntPtr hWnd);

    private DateTime? _firstUnresponsiveTime;
    private int _consecutiveUnresponsiveTicks;

    public bool CheckIfProcessFrozen(Process process, int timeoutSec)
    {
        try
        {
            if (process.HasExited)
            {
                Reset();
                return false;
            }

            bool isUnresponsive = false;

            // Check Windows IsHungAppWindow if window handle exists
            if (process.MainWindowHandle != IntPtr.Zero)
            {
                if (IsHungAppWindow(process.MainWindowHandle))
                {
                    isUnresponsive = true;
                }
            }

            // Check Process.Responding
            if (!isUnresponsive)
            {
                try
                {
                    if (!process.Responding)
                    {
                        isUnresponsive = true;
                    }
                }
                catch
                {
                    // Ignore access exceptions
                }
            }

            if (isUnresponsive)
            {
                _firstUnresponsiveTime ??= DateTime.Now;
                _consecutiveUnresponsiveTicks++;

                var unresponsiveDuration = DateTime.Now - _firstUnresponsiveTime.Value;
                if (unresponsiveDuration.TotalSeconds >= timeoutSec)
                {
                    return true;
                }
            }
            else
            {
                Reset();
            }
        }
        catch
        {
            Reset();
        }

        return false;
    }

    public void Reset()
    {
        _firstUnresponsiveTime = null;
        _consecutiveUnresponsiveTicks = 0;
    }
}
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace VirtualController.Core.Timing;

/// <summary>
/// P/Invoke access to a high-resolution waitable timer (Windows 10 1809 / Server 2019+). Enables
/// sub-millisecond waits in user mode without a driver or a system-wide timer resolution increase
/// (avoids the machine-wide side effects of <c>timeBeginPeriod(1)</c>).
/// </summary>
internal static class WaitableTimerNative
{
    private const uint CREATE_WAITABLE_TIMER_MANUAL_RESET = 0x00000001;
    private const uint CREATE_WAITABLE_TIMER_HIGH_RESOLUTION = 0x00000002;
    private const uint TIMER_ALL_ACCESS = 0x1F0003;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWaitableTimerExW(IntPtr lpTimerAttributes, string? lpTimerName, uint dwFlags, uint dwDesiredAccess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetWaitableTimerEx(
        SafeWaitHandle hTimer,
        in long lpDueTime,
        int lPeriod,
        IntPtr pfnCompletionRoutine,
        IntPtr lpArgToCompletionRoutine,
        IntPtr wakeContext,
        uint tolerableDelay);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(SafeWaitHandle hHandle, uint dwMilliseconds);

    /// <summary>
    /// Creates a high-resolution timer. Returns null if the OS does not support the flag (older Windows
    /// versions), allowing callers to fall back to a standard timer/sleep-based loop.
    /// </summary>
    public static SafeWaitHandle? TryCreate()
    {
        IntPtr raw = CreateWaitableTimerExW(IntPtr.Zero, null,
            CREATE_WAITABLE_TIMER_MANUAL_RESET | CREATE_WAITABLE_TIMER_HIGH_RESOLUTION,
            TIMER_ALL_ACCESS);

        if (raw == IntPtr.Zero)
        {
            return null;
        }

        return new SafeWaitHandle(raw, ownsHandle: true);
    }

    /// <summary>Sets a one-shot relative wait (in 100 ns units; negative means relative) and blocks until it expires.</summary>
    public static void WaitRelative(SafeWaitHandle timer, long dueTimeIn100ns)
    {
        long relative = -Math.Abs(dueTimeIn100ns);
        SetWaitableTimerEx(timer, in relative, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0);
        WaitForSingleObject(timer, uint.MaxValue);
    }
}

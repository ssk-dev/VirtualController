using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace VirtualController.Core.Timing;

/// <summary>
/// P/Invoke-Zugriff auf einen High-Resolution Waitable-Timer (seit Windows 10 1809 / Server 2019).
/// Ermoeglicht ein Warten mit Sub-Millisekunden-Praezision, rein im User-Mode, ohne Treiber oder
/// Systemweite Erhoehung der Timer-Aufloesung (kein "timeBeginPeriod(1)" mit Seiteneffekten auf
/// den gesamten Rechner noetig).
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
    /// Erstellt einen High-Resolution Timer. Gibt null zurueck, wenn das Betriebssystem das
    /// High-Resolution-Flag nicht unterstuetzt (aeltere Windows-Version) - der Aufrufer soll in
    /// diesem Fall auf einen Standard-Timer/Sleep-basierten Loop zurueckfallen.
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

    /// <summary>Setzt den Timer auf eine einmalige relative Wartezeit (in 100ns-Einheiten, negativ = relativ) und wartet blockierend.</summary>
    public static void WaitRelative(SafeWaitHandle timer, long dueTimeIn100ns)
    {
        long relative = -Math.Abs(dueTimeIn100ns);
        SetWaitableTimerEx(timer, in relative, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0);
        WaitForSingleObject(timer, uint.MaxValue);
    }
}

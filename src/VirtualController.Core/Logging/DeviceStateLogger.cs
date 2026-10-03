using System.Text;
using VirtualController.Core.Profiles;

namespace VirtualController.Core.Logging;

/// <summary>
/// Continuously logs one physical device's raw state to a text file until <see cref="Stop"/> is called or the
/// device disconnects, independently of UI visibility or selected tab. Like
/// <see cref="Devices.Hid.RawHidReportReader"/> for benchmarking, it uses the existing API-independent
/// <see cref="Devices.IDeviceReader"/>/<see cref="Devices.DeviceState"/> abstraction rather than the HID-specific
/// <see cref="Devices.Hid.IHidReportSource"/>, so logging works for both XInput and DirectInput devices.
///
/// Logs only actual changes (diff log), not every polling cycle, to keep long sessions from producing huge,
/// unreadable files. Each line is timestamped so temporal relationships (e.g. axis X moving shortly after
/// button Y is pressed) can be examined.
/// </summary>
public sealed class DeviceStateLogger : IDisposable
{
    /// <summary>Polling interval matches <c>InputCaptureService</c> (4 ms, or 250 Hz), much faster than the
    /// UI's 33 ms live display so short inputs such as quick button presses are not missed.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(4);

    /// <summary>Maximum interval between file flushes, limiting log loss to about the last 500 ms if the app
    /// crashes during a session without paying the cost of flushing every line.</summary>
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(500);

    private readonly Devices.PhysicalDeviceInfo _device;
    private readonly string _filePath;
    private readonly CancellationTokenSource _cts = new();
    private Thread? _thread;
    private volatile bool _running;
    private bool _disposed;

    /// <summary>Raised once if the logging loop exits early due to an error (e.g. device disconnected or file
    /// is not writable), not during a normal <see cref="Stop"/> call. Runs off the UI thread.</summary>
    public event Action<Exception>? LogFailed;

    /// <summary>Whether the logging loop is running. Automatically becomes false after an error (see
    /// <see cref="LogFailed"/>).</summary>
    public bool IsRunning => _running;

    /// <summary>Full path to this session's log file.</summary>
    public string FilePath => _filePath;

    /// <param name="device">Device to log.</param>
    /// <param name="filePath">Destination file, or null to use the default path (see <see cref="BuildDefaultFilePath"/>).</param>
    public DeviceStateLogger(Devices.PhysicalDeviceInfo device, string? filePath = null)
    {
        _device = device;
        _filePath = filePath ?? BuildDefaultFilePath(device);
    }

    /// <summary>Builds the default file path from the device display name:
    /// "%AppData%\VirtualController\Logs\log-device-{brand}-{name}.txt". Uses the same brand/name split as
    /// device settings files (see <see cref="FileNaming.SplitBrandAndName"/>) for consistent naming.</summary>
    public static string BuildDefaultFilePath(Devices.PhysicalDeviceInfo device)
    {
        var (brand, name) = FileNaming.SplitBrandAndName(device.DisplayName);
        var directory = Path.Combine(ProfileStore.BaseDirectory, "Logs");
        return Path.Combine(directory, $"log-device-{brand}-{name}.txt");
    }

    public void Start()
    {
        if (_running)
        {
            return;
        }

        _running = true;
        _thread = new Thread(RunLoop)
        {
            Name = "DeviceStateLogger",
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
        };
        _thread.Start();
    }

    public void Stop()
    {
        if (!_running)
        {
            return;
        }

        _running = false;
        _cts.Cancel();
        _thread?.Join(TimeSpan.FromSeconds(2));
    }

    private void RunLoop()
    {
        Devices.IDeviceReader? reader = null;
        StreamWriter? writer = null;

        try
        {
            var directory = Path.GetDirectoryName(_filePath)!;
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Start a fresh file for every session so the diff baseline (see DiffState) does not continue from
            // a potentially stale final state from an earlier session.
            writer = new StreamWriter(_filePath, append: false, Encoding.UTF8) { AutoFlush = false };
            writer.WriteLine($"===== Log started {DateTime.Now:yyyy-MM-dd HH:mm:ss} - Device: {_device.DisplayName} ({_device.Api}, Slot {_device.ApiSlot}) =====");
            writer.Flush();

            reader = Devices.DeviceEnumerator.OpenReader(_device);

            var diffState = new DiffState();
            var lastFlush = DateTime.UtcNow;

            while (!_cts.IsCancellationRequested)
            {
                if (!reader.Poll(out var state))
                {
                    writer.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [Device disconnected - logging stopped]");
                    writer.Flush();
                    break;
                }

                WriteDiff(writer, state, diffState);

                if (DateTime.UtcNow - lastFlush >= FlushInterval)
                {
                    writer.Flush();
                    lastFlush = DateTime.UtcNow;
                }

                _cts.Token.WaitHandle.WaitOne(PollInterval);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal Stop(); not an error.
        }
        catch (Exception ex)
        {
            _running = false;
            LogFailed?.Invoke(ex);
        }
        finally
        {
            try
            {
                writer?.Flush();
            }
            catch
            {
                // The file may no longer be writable (e.g. drive removed); do not let this throw while ending
                // the session.
            }

            writer?.Dispose();
            reader?.Dispose();
            _running = false;
        }
    }

    /// <summary>Stores the last logged raw state so each poll writes only actual changes (see
    /// <see cref="DeviceStateLogger"/> documentation).</summary>
    private sealed class DiffState
    {
        public float[]? LastAxes;
        public bool[]? LastButtons;
        public int LastPov = -1; // -1 = centered/no D-pad, matching the neutral value of DeviceState.Empty.
    }

    private static void WriteDiff(StreamWriter writer, Devices.DeviceState state, DiffState diff)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");

        if (diff.LastAxes is null || diff.LastAxes.Length != state.Axes.Length)
        {
            diff.LastAxes = new float[state.Axes.Length];
            Array.Fill(diff.LastAxes, float.NaN);
        }

        for (int i = 0; i < state.Axes.Length; i++)
        {
            bool isFirstNonZeroReading = float.IsNaN(diff.LastAxes[i]) && state.Axes[i] != 0f;
            if (MathF.Abs(state.Axes[i] - diff.LastAxes[i]) > 0.001f || isFirstNonZeroReading)
            {
                writer.WriteLine($"{timestamp} Axes[{i}] ({(Devices.PhysicalAxisId)i}): {FormatAxis(diff.LastAxes[i])} -> {state.Axes[i]:F3}");
                diff.LastAxes[i] = state.Axes[i];
            }
        }

        if (diff.LastButtons is null || diff.LastButtons.Length != state.Buttons.Length)
        {
            diff.LastButtons = new bool[state.Buttons.Length];
        }

        for (int i = 0; i < state.Buttons.Length; i++)
        {
            if (state.Buttons[i] != diff.LastButtons[i])
            {
                writer.WriteLine($"{timestamp} Buttons[{i}]: {diff.LastButtons[i]} -> {state.Buttons[i]}");
                diff.LastButtons[i] = state.Buttons[i];
            }
        }

        if (state.PovDirectionDegrees != diff.LastPov)
        {
            writer.WriteLine($"{timestamp} PovDirectionDegrees: {diff.LastPov} -> {state.PovDirectionDegrees}");
            diff.LastPov = state.PovDirectionDegrees;
        }
    }

    private static string FormatAxis(float value) => float.IsNaN(value) ? "?" : value.ToString("F3");

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        _cts.Dispose();
    }
}

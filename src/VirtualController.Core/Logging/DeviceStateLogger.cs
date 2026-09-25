using System.Text;
using VirtualController.Core.Profiles;

namespace VirtualController.Core.Logging;

/// <summary>
/// Protokolliert fortlaufend den Rohzustand eines einzelnen physischen Geraets in eine Textdatei, bis
/// <see cref="Stop"/> aufgerufen wird oder das Geraet die Verbindung verliert - unabhaengig von jeglicher
/// UI-Sichtbarkeit (kein Bezug zu einem ausgewaehlten Tab), analog zu <see cref="Devices.Hid.RawHidReportReader"/>
/// fuer das Benchmark-Feature. Nutzt bewusst die bereits vorhandene, API-unabhaengige
/// <see cref="Devices.IDeviceReader"/>/<see cref="Devices.DeviceState"/>-Abstraktion (nicht die
/// HID-spezifische <see cref="Devices.Hid.IHidReportSource"/>-Ebene), da diese Logging-Funktion
/// gleichermassen fuer XInput- wie fuer DirectInput-Geraete verfuegbar sein muss.
///
/// Protokolliert bewusst NUR tatsaechliche Aenderungen (Diff-Log) statt jeden einzelnen Poll-Zyklus,
/// analog zum frueheren, temporaeren Debug-Logging in <c>DeviceConfigDeviceViewModel</c> (siehe
/// Git-Historie) - andernfalls wuerde die Datei bei einer laengeren Sitzung unnoetig gross und
/// unlesbar. Jede Zeile ist zeitgestempelt, damit sich zeitliche Zusammenhaenge (z.B. "Achse X bewegt
/// sich kurz nach Knopfdruck Y") nachvollziehen lassen.
/// </summary>
public sealed class DeviceStateLogger : IDisposable
{
    /// <summary>Abfragerate, bewusst identisch zur Erfassungsrate in <c>InputCaptureService</c> (4ms,
    /// entspricht 250Hz) - deutlich schneller als die 33ms-Live-Anzeige der UI, damit auch kurze
    /// Eingaben (schnelle Knopfdruecke) nicht zwischen zwei Polls verloren gehen.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(4);

    /// <summary>Maximales Intervall zwischen zwei Datei-Flushes, damit bei einem Absturz waehrend einer
    /// laufenden Sitzung hoechstens die letzten ~500ms an Log-Zeilen verloren gehen, ohne bei jeder
    /// einzelnen Zeile einen teuren Flush durchzufuehren.</summary>
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(500);

    private readonly Devices.PhysicalDeviceInfo _device;
    private readonly string _filePath;
    private readonly CancellationTokenSource _cts = new();
    private Thread? _thread;
    private volatile bool _running;
    private bool _disposed;

    /// <summary>Wird genau einmal ausgeloest, wenn die Log-Schleife wegen eines Fehlers (z.B. Geraet
    /// getrennt, Datei nicht schreibbar) vorzeitig beendet wurde - NICHT bei regulaerem <see cref="Stop"/>-Aufruf.
    /// Wird NICHT auf dem UI-Thread ausgeloest.</summary>
    public event Action<Exception>? LogFailed;

    /// <summary>Ob die Log-Schleife aktuell laeuft. Wird nach einem Fehler (siehe <see cref="LogFailed"/>) automatisch false.</summary>
    public bool IsRunning => _running;

    /// <summary>Vollstaendiger Pfad der Log-Datei dieser Sitzung.</summary>
    public string FilePath => _filePath;

    /// <param name="device">Das zu protokollierende Geraet.</param>
    /// <param name="filePath">Zieldatei, oder null fuer den Standardpfad (siehe <see cref="BuildDefaultFilePath"/>).</param>
    public DeviceStateLogger(Devices.PhysicalDeviceInfo device, string? filePath = null)
    {
        _device = device;
        _filePath = filePath ?? BuildDefaultFilePath(device);
    }

    /// <summary>Leitet den Standard-Dateipfad aus dem Anzeigenamen des Geraets ab:
    /// "%AppData%\VirtualController\Logs\log-device-{marke}-{name}.txt" - dieselbe Marke/Name-Aufteilung
    /// wie bei den Geraete-Einstellungsdateien (siehe <see cref="FileNaming.SplitBrandAndName"/>), damit
    /// beide Dateiarten fuer denselben Geraetenamen konsistent benannt sind.</summary>
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

            // Bewusst kein Append: jede gestartete Sitzung beginnt mit einer frischen Datei, damit die
            // Diff-Basis (siehe DiffState) nicht faelschlich an einen alten, moeglicherweise veralteten
            // Endzustand einer fruehreren Sitzung anschliesst.
            writer = new StreamWriter(_filePath, append: false, Encoding.UTF8) { AutoFlush = false };
            writer.WriteLine($"===== Log gestartet {DateTime.Now:yyyy-MM-dd HH:mm:ss} - Geraet: {_device.DisplayName} ({_device.Api}, Slot {_device.ApiSlot}) =====");
            writer.Flush();

            reader = Devices.DeviceEnumerator.OpenReader(_device);

            var diffState = new DiffState();
            var lastFlush = DateTime.UtcNow;

            while (!_cts.IsCancellationRequested)
            {
                if (!reader.Poll(out var state))
                {
                    writer.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [Geraet getrennt - Log beendet]");
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
            // Regulaerer Stop() - kein Fehler.
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
                // Datei ggf. nicht mehr schreibbar (z.B. Datentraeger entfernt) - beim Beenden der
                // Sitzung darf dies keine weitere Ausnahme nach aussen werfen.
            }

            writer?.Dispose();
            reader?.Dispose();
            _running = false;
        }
    }

    /// <summary>Haelt den zuletzt protokollierten Rohzustand fest, um bei jedem Poll nur tatsaechliche
    /// Aenderungen zu schreiben (siehe Klassendokumentation von <see cref="DeviceStateLogger"/>).</summary>
    private sealed class DiffState
    {
        public float[]? LastAxes;
        public bool[]? LastButtons;
        public int LastPov = -1; // -1 = zentriert/kein D-Pad, identisch zum Neutralwert von DeviceState.Empty.
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

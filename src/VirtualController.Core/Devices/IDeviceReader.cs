namespace VirtualController.Core.Devices;

/// <summary>
/// Einheitliche Lese-Schnittstelle fuer ein physisches Eingabegeraet, unabhaengig von der
/// zugrunde liegenden API (XInput oder DirectInput). Implementierungen muessen threadsicher
/// fuer wiederholte <see cref="Poll"/>-Aufrufe aus einem dedizierten Polling-Thread sein.
/// </summary>
public interface IDeviceReader : IDisposable
{
    PhysicalDeviceInfo Info { get; }

    /// <summary>Liest den aktuellen Zustand. Gibt false zurueck, wenn das Geraet nicht mehr verbunden ist.</summary>
    bool Poll(out DeviceState state);
}

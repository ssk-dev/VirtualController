using HidSharp;

namespace VirtualController.Core.Devices.Hid;

/// <summary>
/// <see cref="IHidReportSource"/>-Implementierung auf Basis eines bereits geoeffneten
/// <see cref="HidSharp.HidStream"/> (siehe <see cref="HidDeviceInfoReader.TryOpenReportSource"/>) - die
/// einzige Stelle, an der <see cref="HidSharp.HidStream"/> direkt verwendet wird (siehe Kapselungs-Hinweis
/// in <see cref="IHidReportSource"/>).
///
/// <see cref="HidStream.Read(byte[])"/> blockiert bereits nativ, bis ein Report eintrifft oder der Stream
/// geschlossen wird - es ist daher KEIN eigenes Polling/Timeout-Handling notwendig. Der uebergebene
/// <see cref="CancellationToken"/> wird ausschliesslich ueber das Schliessen des Streams wirksam (siehe
/// <see cref="ReadReport"/>), da <see cref="HidStream.Read(byte[])"/> selbst keinen Token annimmt.
/// </summary>
internal sealed class HidSharpReportSource : IHidReportSource
{
    private readonly HidStream _stream;
    private readonly byte[] _buffer;
    private bool _disposed;

    public int InputReportLength { get; }

    internal HidSharpReportSource(HidStream stream, int inputReportLength)
    {
        _stream = stream;
        InputReportLength = inputReportLength;
        _buffer = new byte[Math.Max(inputReportLength, 1)];

        // WICHTIG: HidSharp setzt HidStream.ReadTimeout intern standardmaessig auf 3000ms (nicht,
        // wie urspruenglich hier angenommen, auf "kein Timeout") - ohne diese Zeile wuerde Read()
        // nach 3 Sekunden ohne neuen Report eine TimeoutException werfen ("Operation timed out
        // (3000 ms)."), selbst wenn das Geraet lediglich ruhig gehalten wird (z.B. ein unbewegter
        // Analogstick waehrend eines Benchmarks/Logs). Ein blockierender Read() bis zum naechsten
        // TATSAECHLICHEN Report ist fuer die Timing-Kennzahlen (Polling-Rate/Intervall) das
        // gewuenschte Verhalten - daher hier explizit auf unendlich gesetzt.
        _stream.ReadTimeout = Timeout.Infinite;
    }

    public HidReport ReadReport(CancellationToken cancellationToken)
    {
        // HidStream.Read() selbst kennt keinen CancellationToken. Ein per Registrierung ausgeloestes
        // Schliessen des Streams laesst den blockierenden Read() mit einer IOException zurueckkehren,
        // was wir hier in ein reguläres OperationCanceledException uebersetzen - siehe Stop()-Ablauf des
        // zukuenftigen Benchmark-Orchestrators, der Dispose() dieser Quelle aus einem anderen Thread
        // aufruft, um den blockierten Lese-Thread zeitnah zu wecken statt auf den naechsten Report zu warten.
        using var registration = cancellationToken.Register(static state => ((HidSharpReportSource)state!).SafeCloseStream(), this);

        cancellationToken.ThrowIfCancellationRequested();

        int bytesRead;
        try
        {
            bytesRead = _stream.Read(_buffer);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        long timestamp = HidReport.CaptureTimestampTicks();

        var data = new byte[bytesRead];
        Array.Copy(_buffer, data, bytesRead);
        return new HidReport(data, timestamp);
    }

    private void SafeCloseStream()
    {
        try
        {
            _stream.Close();
        }
        catch
        {
            // Stream ggf. bereits geschlossen/das Geraet getrennt - fuer den hier verfolgten Zweck
            // (blockierenden Read() zeitnah wecken) unerheblich.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SafeCloseStream();
    }
}

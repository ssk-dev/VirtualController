using HidSharp;

namespace VirtualController.Core.Devices.Hid;

/// <summary>
/// <see cref="IHidReportSource"/> implementation backed by an already-open
/// <see cref="HidSharp.HidStream"/> (see <see cref="HidDeviceInfoReader.TryOpenReportSource"/>). This is the
/// only place that directly uses <see cref="HidSharp.HidStream"/> (see the encapsulation note in
/// <see cref="IHidReportSource"/>).
///
/// <see cref="HidStream.Read(byte[])"/> blocks natively until a report arrives or the stream is closed, so no
/// custom polling or timeout handling is needed. The supplied <see cref="CancellationToken"/> takes effect
/// only by closing the stream (see <see cref="ReadReport"/>), because HidStream.Read does not accept a token.
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

        // IMPORTANT: HidSharp defaults HidStream.ReadTimeout to 3000 ms, not infinite. Without this, Read()
        // would throw a TimeoutException after three seconds without a report, even when the device is simply
        // idle (e.g. a motionless analog stick during a benchmark/log). Blocking until the next actual report
        // is required for timing metrics, so explicitly set the timeout to infinite.
        _stream.ReadTimeout = Timeout.Infinite;
    }

    public HidReport ReadReport(CancellationToken cancellationToken)
    {
        // HidStream.Read() does not accept a CancellationToken. Closing the stream through this registration
        // makes the blocking Read() return an IOException, which is translated to OperationCanceledException.
        // BenchmarkSession.Stop disposes this source from another thread to wake the reader promptly instead
        // of waiting for the next report.
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
            // The stream may already be closed or the device disconnected; either way, the goal of promptly
            // waking the blocked Read() has been met.
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

using VirtualController.Core.Devices.Hid;

namespace VirtualController.Core.Devices.Usb;

/// <summary>
/// Verbindet die HID-Ebene (<see cref="HidDeviceInfo"/>, insbesondere <see cref="HidDeviceInfo.UsbHubDevicePath"/>/
/// <see cref="HidDeviceInfo.UsbPortNumber"/>) mit der rohen USB-Hub-IOCTL-Abfrage (<see cref="UsbHubNativeInterop"/>)
/// zu einer einzigen, defensiven Einstiegsmethode fuer die Transport-Kennzahlen des geplanten
/// Geraete-Benchmarks (USB Speed, Endpoint-Informationen, Nominal Polling Rate).
///
/// Liefert bei JEDEM Fehler (fehlende Hub-Pfad-Information, Hub nicht erreichbar, IOCTL schlaegt fehl)
/// bewusst null statt einer Ausnahme - der Aufrufer (spaeterer Benchmark-Orchestrator) muss diesen Teil
/// der Kennzahlen dann als "nicht ermittelbar" anzeigen, statt den gesamten Benchmark abzubrechen (siehe
/// Klassendokumentation von <see cref="UsbHubNativeInterop"/> zur Unsicherheit dieses ungetesteten Codes).
/// </summary>
public static class UsbTopologyResolver
{
    /// <summary>
    /// Ermittelt USB-Speed, Endpoints und (bereits speed-korrigiertes) nominales Polling-Intervall fuer
    /// das durch <paramref name="hidInfo"/> beschriebene Geraet. Gibt null zurueck, falls
    /// <see cref="HidDeviceInfo.UsbHubDevicePath"/>/<see cref="HidDeviceInfo.UsbPortNumber"/> nicht
    /// verfuegbar sind (siehe <see cref="HidDeviceInfoReader"/>) oder die native Abfrage fehlschlaegt.
    /// </summary>
    public static UsbConnectionInfo? TryResolve(HidDeviceInfo hidInfo)
    {
        if (hidInfo.UsbHubDevicePath is null || hidInfo.UsbPortNumber is not { } portNumber)
        {
            return null;
        }

        var raw = UsbHubNativeInterop.TryGetNodeConnectionInformation(hidInfo.UsbHubDevicePath, portNumber);
        if (raw is null)
        {
            return null;
        }

        return ApplySpeedCorrection(raw);
    }

    /// <summary>
    /// <see cref="UsbHubNativeInterop"/> berechnet <see cref="UsbEndpointInfo.NominalPollingIntervalMs"/>
    /// zunaechst unter der Annahme einer Low-/Full-Speed-Kodierung von <c>bInterval</c> (direkter
    /// Millisekunden-Wert), da ihr zum Zeitpunkt der Endpoint-Auswertung die Geschwindigkeit des
    /// Geraets noch nicht getrennt vorliegt. Bei High-/SuperSpeed-Geraeten gilt stattdessen die
    /// Mikroframe-Formel <c>2^(bInterval-1) * 0.125 ms</c> (USB-2.0-Spezifikation Abschnitt 9.6.6) -
    /// diese Korrektur wird hier, nachdem <see cref="UsbConnectionInfo.Speed"/> bekannt ist, nachtraeglich
    /// auf alle Endpoints angewendet.
    /// </summary>
    private static UsbConnectionInfo ApplySpeedCorrection(UsbConnectionInfo raw)
    {
        if (raw.Speed != UsbSpeed.High && raw.Speed != UsbSpeed.Super)
        {
            // Low/Full/Unknown: bereits korrekt (direkter ms-Wert) berechnet.
            return raw;
        }

        var correctedEndpoints = raw.Endpoints
            .Select(e => e with
            {
                NominalPollingIntervalMs = e.IntervalRaw > 0
                    ? Math.Pow(2, e.IntervalRaw - 1) * 0.125
                    : 0,
            })
            .ToList();

        return raw with { Endpoints = correctedEndpoints };
    }
}

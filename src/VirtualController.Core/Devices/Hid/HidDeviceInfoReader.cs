using HidSharp;

namespace VirtualController.Core.Devices.Hid;

/// <summary>
/// Loest Vendor-/Product-ID (siehe <see cref="PhysicalDeviceInfo.VendorId"/>/<see cref="PhysicalDeviceInfo.ProductId"/>)
/// auf konkrete HID-Kenndaten (<see cref="HidDeviceInfo"/>) auf und oeffnet daraus bei Bedarf eine rohe
/// Report-Quelle (<see cref="IHidReportSource"/>) - die einzige Stelle in diesem Projekt, an der der Typ
/// <see cref="HidSharp.HidDevice"/> direkt verwendet wird (siehe Kapselungs-Hinweis in
/// <see cref="IHidReportSource"/>).
///
/// WICHTIGE EINSCHRAENKUNG (siehe auch <see cref="HidDeviceInfo.ReleaseNumberBcd"/>): HID selbst kennt
/// keinen eigenen "Firmware-Version"-String-Deskriptor. <see cref="HidDeviceInfo.ReleaseNumberBcd"/>
/// (HidD_GetAttributes -> <c>VersionNumber</c>) ist lediglich eine vom Hersteller frei vergebene,
/// BCD-codierte Zahl, die üblicherweise, aber nicht garantiert, die Firmware-/Hardware-Revision
/// widerspiegelt - manche Geraete melden hier durchgehend 0x0100 oder 0x0000, unabhaengig von der
/// tatsaechlichen Firmware. Diese Zahl darf im Benchmark-Ergebnis daher nur als Naeherung
/// ("Firmware-Version (laut Geraet, ungeprueft)"), nicht als verlaessliche Angabe dargestellt werden.
///
/// Ebenso liefert HidSharp bei mehreren gleichzeitig angeschlossenen, identischen Geraeten (gleiche
/// VID/PID) mehrere Treffer - <see cref="TryResolve"/> waehlt in diesem Fall bewusst KEINEN automatisch
/// aus, sondern erwartet den bereits per Seriennummer oder Geraetepfad disambiguierten Aufruf, sofern
/// mehrere Kandidaten vorliegen (siehe <see cref="TryResolveAll"/>).
/// </summary>
public static class HidDeviceInfoReader
{
    /// <summary>
    /// Liefert alle aktuell angeschlossenen HID-Geraete mit passender Vendor-/Product-ID. Kann mehr als
    /// ein Ergebnis liefern (siehe Klassendokumentation) - der Aufrufer (z.B. das Benchmark-Feature)
    /// muss bei mehreren Ergebnissen selbst entscheiden, welches Geraet gemeint ist (z.B. ueber
    /// Seriennummer, falls vorhanden, oder Nutzer-Rueckfrage).
    /// </summary>
    public static IReadOnlyList<HidDeviceInfo> TryResolveAll(ushort vendorId, ushort productId)
    {
        var result = new List<HidDeviceInfo>();

        IEnumerable<HidDevice> candidates;
        try
        {
            candidates = DeviceList.Local.GetHidDevices(vendorId, productId);
        }
        catch
        {
            // HidSharp/Betriebssystem-Zugriff auf die Geraeteliste kann fehlschlagen (z.B. fehlende
            // Berechtigung) - in diesem Fall gilt schlicht "kein Geraet gefunden" statt Absturz.
            return result;
        }

        foreach (var device in candidates)
        {
            var info = TryReadInfo(device);
            if (info is not null)
            {
                result.Add(info);
            }
        }

        return result;
    }

    /// <summary>Bequemlichkeitsmethode fuer den (haeufigen) Fall genau eines passenden Geraets - siehe
    /// <see cref="TryResolveAll"/> fuer den Umgang mit mehreren gleichzeitig angeschlossenen, identischen
    /// Geraeten.</summary>
    public static HidDeviceInfo? TryResolve(ushort vendorId, ushort productId)
    {
        var all = TryResolveAll(vendorId, productId);
        return all.Count > 0 ? all[0] : null;
    }

    /// <summary>Oeffnet eine rohe Report-Quelle fuer das Geraet am angegebenen Betriebssystem-Geraetepfad
    /// (siehe <see cref="HidDeviceInfo.DevicePath"/>). Gibt null zurueck, falls das Geraet nicht (mehr)
    /// gefunden wird oder das Oeffnen fehlschlaegt (z.B. weil es bereits exklusiv von einer anderen
    /// Anwendung geoeffnet ist).</summary>
    public static IHidReportSource? TryOpenReportSource(string devicePath)
    {
        HidDevice? device;
        try
        {
            device = DeviceList.Local.GetHidDevices().FirstOrDefault(d => d.DevicePath == devicePath);
        }
        catch
        {
            return null;
        }

        if (device is null)
        {
            return null;
        }

        try
        {
            if (!device.TryOpen(out HidStream stream))
            {
                return null;
            }

            return new HidSharpReportSource(stream, device.GetMaxInputReportLength());
        }
        catch
        {
            // Geraet ggf. waehrend des Oeffnens getrennt worden, oder exklusiv durch eine andere
            // Anwendung gesperrt (z.B. ein laufendes Spiel) - beides ist ein normaler, erwartbarer
            // Fall und darf nicht zum Absturz der Anwendung fuehren.
            return null;
        }
    }

    private static HidDeviceInfo? TryReadInfo(HidDevice device)
    {
        try
        {
            int? usbPortNumber = null;
            string? usbHubDevicePath = null;
            try
            {
                var usbPort = device.GetUsbPort();
                if (usbPort is not null)
                {
                    usbPortNumber = usbPort.PortNumber;
                    usbHubDevicePath = usbPort.HubDevicePath;
                }
            }
            catch
            {
                // USB-Topologie-Information ist ein "Nice to have" (siehe UsbTopologyResolver) -
                // ihr Fehlen darf die restlichen, bereits erfolgreich gelesenen Kenndaten nicht verwerfen.
            }

            return new HidDeviceInfo(
                VendorId: (ushort)device.VendorID,
                ProductId: (ushort)device.ProductID,
                Manufacturer: TryReadOptionalString(() => device.GetManufacturer()),
                ProductName: TryReadOptionalString(() => device.GetProductName()),
                SerialNumber: TryReadOptionalString(() => device.GetSerialNumber()),
                ReleaseNumberBcd: device.ReleaseNumberBcd,
                MaxInputReportLength: device.GetMaxInputReportLength(),
                MaxOutputReportLength: device.GetMaxOutputReportLength(),
                MaxFeatureReportLength: device.GetMaxFeatureReportLength(),
                DevicePath: device.DevicePath,
                UsbPortNumber: usbPortNumber,
                UsbHubDevicePath: usbHubDevicePath);
        }
        catch
        {
            // Ein einzelnes, nicht (mehr) lesbares Geraet (z.B. gerade getrennt) darf die Ermittlung
            // der uebrigen, weiterhin gueltigen Kandidaten nicht verhindern (siehe TryResolveAll).
            return null;
        }
    }

    /// <summary>Viele der optionalen HID-String-Deskriptoren (Manufacturer/Product/SerialNumber) werfen
    /// bei fehlendem Deskriptor oder Zugriffsproblemen eine Ausnahme statt null zurueckzugeben - dieser
    /// Wrapper vereinheitlicht das auf "null bei jeglichem Fehler", da das Fehlen eines einzelnen
    /// optionalen Strings kein Grund ist, die gesamte Geraete-Auskunft zu verwerfen.</summary>
    private static string? TryReadOptionalString(Func<string> read)
    {
        try
        {
            return read();
        }
        catch
        {
            return null;
        }
    }
}

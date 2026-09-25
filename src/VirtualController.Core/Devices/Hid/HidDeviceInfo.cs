namespace VirtualController.Core.Devices.Hid;

/// <summary>
/// Statische, sich waehrend der Verbindungsdauer eines Geraets nicht mehr aendernde HID-Kenndaten -
/// Grundlage fuer die Identification-/Transport-Kennzahlen des geplanten Geraete-Benchmarks (siehe
/// <see cref="HidDeviceInfoReader"/>). Enthaelt bewusst ausschliesslich primitive Typen/Strings, damit
/// diese Klasse ausserhalb dieses <c>Devices.Hid</c>-Unterordners verwendet werden kann, ohne dass die
/// zugrunde liegende Zugriffsbibliothek (aktuell HidSharp) an anderer Stelle im Code sichtbar wird.
/// </summary>
/// <param name="VendorId">USB Vendor ID, siehe <see cref="PhysicalDeviceInfo.VendorId"/>.</param>
/// <param name="ProductId">USB Product ID, siehe <see cref="PhysicalDeviceInfo.ProductId"/>.</param>
/// <param name="Manufacturer">Hersteller-String-Deskriptor, falls vom Geraet bereitgestellt und lesbar - sonst null.</param>
/// <param name="ProductName">Produkt-String-Deskriptor, falls vom Geraet bereitgestellt und lesbar - sonst null.</param>
/// <param name="SerialNumber">Seriennummer-String-Deskriptor, falls vom Geraet bereitgestellt und lesbar - sonst null.</param>
/// <param name="ReleaseNumberBcd">Vom Geraet gemeldete BCD-codierte Versionsnummer (<c>bcdDevice</c>) - naeherungsweise
/// als "Firmware-Version" interpretierbar, ABER kein garantiert menschenlesbarer Firmware-String (siehe
/// Klassendokumentation von <see cref="HidDeviceInfoReader"/> fuer die genaue Einschraenkung).</param>
/// <param name="MaxInputReportLength">Nominal-Laenge eines Input-Reports in Byte (Transport-Kennzahl "Report Size").</param>
/// <param name="MaxOutputReportLength">Nominal-Laenge eines Output-Reports in Byte.</param>
/// <param name="MaxFeatureReportLength">Nominal-Laenge eines Feature-Reports in Byte.</param>
/// <param name="DevicePath">Betriebssystem-Geraetepfad (fuer Diagnosezwecke/Weiterverarbeitung, z.B. <see cref="UsbTopologyResolver"/>).</param>
/// <param name="UsbPortNumber">Physische Port-Nummer am uebergeordneten USB-Hub, falls ermittelbar - sonst null.</param>
/// <param name="UsbHubDevicePath">Geraetepfad des uebergeordneten USB-Hubs, falls ermittelbar - sonst null. Grundlage
/// fuer die spaeter geplante <see cref="UsbTopologyResolver"/>-Abfrage von USB-Speed/Endpoint/Polling-Intervall.</param>
public sealed record HidDeviceInfo(
    ushort VendorId,
    ushort ProductId,
    string? Manufacturer,
    string? ProductName,
    string? SerialNumber,
    int ReleaseNumberBcd,
    int MaxInputReportLength,
    int MaxOutputReportLength,
    int MaxFeatureReportLength,
    string DevicePath,
    int? UsbPortNumber,
    string? UsbHubDevicePath);

using VirtualController.Core.Devices;

namespace VirtualController.App.ViewModels;

/// <summary>
/// Baut einen menschenlesbaren Anzeigenamen fuer eine physische Eingabe ("Geraetename - Eingabename")
/// anhand von DeviceId/Kind/Index, unter Beruecksichtigung eines vom Nutzer vergebenen individuellen
/// Namens (siehe <see cref="DeviceSettings"/>) sowie des zuletzt bekannten Geraetenamens, falls das
/// Geraet aktuell nicht angeschlossen ist. Zentrale Stelle fuer diese Logik, damit Mapping-Zeilen
/// (<see cref="MappingRowViewModel"/>) und Modus-Umschalt-Ausloeser (<see cref="ModeViewModel"/>,
/// <see cref="VirtualControllerViewModel"/>) sie nicht jeweils eigenstaendig duplizieren.
/// </summary>
public static class PhysicalInputDisplayNameHelper
{
    public static string Build(
        string deviceId,
        PhysicalInputKind kind,
        int index,
        IReadOnlyList<PhysicalDeviceInfo> knownDevices,
        IReadOnlyDictionary<string, DeviceSettings> deviceSettings,
        out bool isConnected)
    {
        // Noch keine physische Quelle zugewiesen (frisch angelegte Mapping-Zeile, bevor "Erfassen"
        // oder "Zuweisen" benutzt wurde): weder ein sinnvoller Anzeigename noch ein "nicht verbunden"-
        // Hinweis sind hier zutreffend - es handelt sich nicht um ein getrenntes Geraet, sondern schlicht
        // um eine noch offene Auswahl. isConnected wird bewusst auf true gesetzt, damit der rote
        // "(nicht verbunden)"-Hinweis in der View (an IsSourceConnected gebunden) nicht angezeigt wird.
        if (string.IsNullOrEmpty(deviceId))
        {
            isConnected = true;
            return string.Empty;
        }

        var device = knownDevices.FirstOrDefault(d => d.DeviceId == deviceId);
        isConnected = device is not null;

        string deviceName = device?.DisplayName
            ?? (deviceSettings.TryGetValue(deviceId, out var settings) ? settings.LastKnownDisplayName : null)
            ?? deviceId;

        var storageKey = PhysicalInputCatalog.BuildStorageKey(deviceId, kind, index);
        string? customInputName = deviceSettings.TryGetValue(deviceId, out var deviceSettingsEntry)
            && deviceSettingsEntry.Inputs.TryGetValue(storageKey, out var inputSettings)
            ? inputSettings.CustomName
            : null;

        string inputName = customInputName
            ?? (device is not null
                ? PhysicalInputCatalog.BuildInputs(device).FirstOrDefault(i => i.Kind == kind && i.Index == index)?.DisplayName
                : null)
            ?? $"{kind} {index}";

        return $"{deviceName} \u2013 {inputName}";
    }
}

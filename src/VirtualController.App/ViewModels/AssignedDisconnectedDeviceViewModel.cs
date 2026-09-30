namespace VirtualController.App.ViewModels;

/// <summary>
/// Repraesentiert ein physisches Geraet, das einem virtuellen Controller zugeordnet ist (siehe
/// <see cref="Core.Mapping.VirtualControllerProfile.AssignedDeviceIds"/>), aber aktuell nicht
/// angeschlossen ist. Wird in der Liste "Zugewiesene Geräte" unterhalb der "Verfügbaren Geräte"
/// ausgegraut angezeigt (siehe <see cref="VirtualControllerViewModel.AssignedDisconnectedDeviceSelections"/>),
/// damit der Nutzer erkennt, dass die Zuweisung weiterhin besteht, auch waehrend das Geraet getrennt
/// ist - rein informativ, ohne Interaktionsmoeglichkeit (im Gegensatz zu <see cref="DeviceSelectionViewModel"/>,
/// das nur fuer tatsaechlich angeschlossene Geraete verwendet wird).
/// </summary>
public sealed class AssignedDisconnectedDeviceViewModel
{
    /// <summary>Eindeutige Geraete-Id (siehe <see cref="Core.Devices.PhysicalDeviceInfo.DeviceId"/>).</summary>
    public string DeviceId { get; }

    /// <summary>Zuletzt bekannter Anzeigename (siehe <see cref="Core.Devices.DeviceSettings.LastKnownDisplayName"/>),
    /// oder die rohe <see cref="DeviceId"/>, falls noch nie ein Anzeigename erfasst wurde.</summary>
    public string DisplayName { get; }

    public AssignedDisconnectedDeviceViewModel(string deviceId, string displayName)
    {
        DeviceId = deviceId;
        DisplayName = displayName;
    }
}

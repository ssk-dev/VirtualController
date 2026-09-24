using System.Collections.ObjectModel;

namespace VirtualController.App.ViewModels;

/// <summary>
/// Fasst die physischen Eingaben eines Geraets im Konfigurationsdialog zu einer benannten Gruppe
/// zusammen (z.B. "Buttons", "D-Pad"), damit der Nutzer bei Geraeten mit vielen Eingaben
/// schneller die gesuchte Kategorie findet, statt eine lange, unstrukturierte Liste zu durchsuchen.
/// Fuer Achsen wird stattdessen <see cref="DeviceConfigAxisGroupViewModel"/> verwendet, da dort
/// Positiv-/Negativ-Eintraege paarweise mit gemeinsamem Rahmen dargestellt werden.
/// </summary>
public sealed class DeviceConfigInputGroupViewModel : IDeviceConfigGroup
{
    public string GroupName { get; }

    public ObservableCollection<DeviceConfigInputRowViewModel> Inputs { get; } = new();

    public IEnumerable<DeviceConfigInputRowViewModel> AllRows => Inputs;

    public DeviceConfigInputGroupViewModel(string groupName)
    {
        GroupName = groupName;
    }
}

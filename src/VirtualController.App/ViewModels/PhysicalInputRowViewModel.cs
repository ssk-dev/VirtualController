using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VirtualController.Core.Devices;
using VirtualController.Core.Virtual;

namespace VirtualController.App.ViewModels;

/// <summary>
/// Eine einzelne Zeile in der aufklappbaren Liste der physischen Eingaben eines Geraets:
/// zeigt den (ggf. vom Nutzer umbenannten) Anzeigenamen, hebt sich farblich hervor, waehrend
/// die zugehoerige physische Eingabe gerade aktiv ist (Live-Highlight), und bietet eine
/// "Zuweisen"-Aktion, um diese Eingabe direkt als Quelle einer neuen Mapping-Zeile zu uebernehmen.
/// </summary>
public sealed partial class PhysicalInputRowViewModel : ObservableObject
{
    /// <summary>Ab diesem absoluten Achsenausschlag gilt eine Achsen-Richtung als "aktiv" (lockerer als der Schwellwert der Erfassung, fuer eine reaktionsfreudige Live-Anzeige).</summary>
    private const float AxisActiveThreshold = 0.3f;

    public PhysicalInputRef Ref { get; }

    private readonly Action<PhysicalInputRef, string> _saveCustomName;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private bool _isActive;

    /// <summary>Ob diese physische Eingabe im Geraete-Konfigurationsdialog aktiviert ist. Wird nur bei
    /// der Erstellung dieser Zeile ausgewertet (Snapshot) - reicht aus, da bei jeder Aenderung der
    /// Geraeteeinstellungen (<see cref="MainViewModel.NotifyDeviceSettingsChanged"/>) die komplette
    /// Geraeteauswahlliste inkl. dieser Zeilen ohnehin neu aufgebaut wird. Wird in der View genutzt,
    /// um deaktivierte Eingaben visuell auszugrauen.</summary>
    [ObservableProperty]
    private bool _isEnabled;

    /// <summary>Wird ausgeloest, wenn der Nutzer diese physische Eingabe einer neuen Mapping-Zeile zuweisen moechte.</summary>
    public event Action<PhysicalInputRef>? AssignRequested;

    public PhysicalInputRowViewModel(PhysicalInputRef inputRef, string displayName, bool isEnabled, Action<PhysicalInputRef, string> saveCustomName)
    {
        Ref = inputRef;
        _saveCustomName = saveCustomName;
        _name = displayName;
        _isEnabled = isEnabled;
    }

    /// <summary>Aktualisiert den Aktiv-Status anhand des aktuellen Geraetezustands, fuer die Live-Hervorhebung in der UI.</summary>
    public void UpdateActiveState(DeviceState state)
    {
        IsActive = Ref.Kind switch
        {
            PhysicalInputKind.Button => Ref.Index < state.Buttons.Length && state.Buttons[Ref.Index],
            PhysicalInputKind.AxisPositive => state.GetAxisRaw(Ref.Index) >= AxisActiveThreshold,
            PhysicalInputKind.AxisNegative => state.GetAxisRaw(Ref.Index) <= -AxisActiveThreshold,
            PhysicalInputKind.DPad => state.PovDirectionDegrees >= 0,
            PhysicalInputKind.DPadUp => DPadDirectionExtensions.FromPovDegrees(state.PovDirectionDegrees).HasUp(),
            PhysicalInputKind.DPadDown => DPadDirectionExtensions.FromPovDegrees(state.PovDirectionDegrees).HasDown(),
            PhysicalInputKind.DPadLeft => DPadDirectionExtensions.FromPovDegrees(state.PovDirectionDegrees).HasLeft(),
            PhysicalInputKind.DPadRight => DPadDirectionExtensions.FromPovDegrees(state.PovDirectionDegrees).HasRight(),
            _ => false
        };
    }

    [RelayCommand]
    private void Assign() => AssignRequested?.Invoke(Ref);

    partial void OnNameChanged(string value) => _saveCustomName(Ref, value);
}

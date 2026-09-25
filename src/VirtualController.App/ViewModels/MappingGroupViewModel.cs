using System.Collections.ObjectModel;
using VirtualController.Core.Mapping;

namespace VirtualController.App.ViewModels;

/// <summary>
/// Eine einzelne Gruppe der nach Ziel-Typ gruppierten Mapping-Tabelle (siehe
/// <see cref="ModeViewModel.MappingGroups"/>): fasst alle Mapping-Zeilen desselben
/// <see cref="MappingTargetKind"/> zusammen und stellt eine Ueberschrift fuer die Anzeige in der UI
/// bereit. Eine Gruppe existiert in <see cref="ModeViewModel.MappingGroups"/> ausschliesslich, solange
/// mindestens eine Zeile diesem Ziel-Typ zugeordnet ist (siehe <see cref="ModeViewModel.RebuildMappingGroups"/>) -
/// leere Gruppen werden nicht angezeigt.
/// </summary>
public sealed class MappingGroupViewModel
{
    /// <summary>Der Ziel-Typ, fuer den diese Gruppe steht - identisch mit <see cref="MappingRowViewModel.SelectedTargetKind"/>
    /// aller in <see cref="Rows"/> enthaltenen Zeilen.</summary>
    public MappingTargetKind Kind { get; }

    /// <summary>Anzeigename der Gruppenueberschrift - identisch mit dem Text, den die "Ziel-Typ"-ComboBox
    /// jeder Zeile fuer denselben Wert anzeigt (reines Enum.ToString(), keine zusaetzliche Uebersetzung),
    /// damit Gruppenname und Dropdown-Eintrag fuer den Nutzer eindeutig demselben Ziel-Typ zuzuordnen sind.</summary>
    public string Header { get; }

    /// <summary>Alle Mapping-Zeilen mit <see cref="MappingRowViewModel.SelectedTargetKind"/> == <see cref="Kind"/>,
    /// in derselben Reihenfolge wie in der zugrunde liegenden <see cref="ModeViewModel.Mappings"/>-Liste.</summary>
    public ObservableCollection<MappingRowViewModel> Rows { get; } = new();

    public MappingGroupViewModel(MappingTargetKind kind, string header)
    {
        Kind = kind;
        Header = header;
    }
}

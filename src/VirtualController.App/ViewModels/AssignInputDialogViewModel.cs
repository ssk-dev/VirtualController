using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VirtualController.App.ViewModels;

/// <summary>
/// ViewModel des modalen "Zuweisen"-Dialogs (<see cref="Views.AssignInputDialog"/>): zeigt alle
/// vom aufrufenden <see cref="MappingRowViewModel"/> ermittelten physischen Eingaben
/// (<see cref="MappingRowViewModel.BuildAssignableInputs"/>) nach Geraet gruppiert an und erlaubt
/// das Filtern per Live-Suchfeld, bevor der Nutzer eine Eingabe als neue physische Quelle der
/// Mapping-Zeile bestaetigt - eine Alternative zum physischen Erfassen ("Erfassen"-Button).
/// </summary>
public sealed partial class AssignInputDialogViewModel : ObservableObject
{
    private readonly List<AssignableInputOption> _allInputs;

    /// <summary>Aktueller Suchtext. Filtert <see cref="View"/> live bei jeder Eingabe (siehe
    /// <see cref="OnSearchTextChanged"/>), sowohl gegen den Anzeigenamen der Eingabe als auch gegen
    /// den Geraetenamen, damit z.B. auch nach dem Geraet selbst gesucht werden kann.</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>Aktuell in der Liste ausgewaehlte Eingabe. Bestimmt, ob <see cref="ConfirmCommand"/>
    /// ausfuehrbar ist - eine leere Auswahl kann nicht bestaetigt werden.</summary>
    [ObservableProperty]
    private AssignableInputOption? _selectedInput;

    /// <summary>Gefilterte, nach <see cref="AssignableInputOption.DeviceDisplayName"/> gruppierte Sicht
    /// auf alle uebergebenen Eingaben, direkt an die <c>ListBox</c> der View gebunden.</summary>
    public ICollectionView View { get; }

    /// <summary>Wird ausgeloest, wenn der Nutzer die aktuelle Auswahl bestaetigt hat (OK-Button,
    /// Enter oder Doppelklick auf einen Listeneintrag) - der Code-Behind des Dialogs schliesst das
    /// Fenster daraufhin mit <c>DialogResult = true</c>.</summary>
    public event Action? Confirmed;

    /// <summary>Wird ausgeloest, wenn der Nutzer den Dialog abbricht (Abbrechen-Button oder Escape) -
    /// der Code-Behind des Dialogs schliesst das Fenster daraufhin mit <c>DialogResult = false</c>.</summary>
    public event Action? Cancelled;

    public AssignInputDialogViewModel(IReadOnlyList<AssignableInputOption> allInputs)
    {
        _allInputs = allInputs
            .OrderBy(i => i.DeviceDisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(i => i.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        View = new ListCollectionView(_allInputs) { Filter = FilterPredicate };
    }

    private bool FilterPredicate(object obj)
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        return obj is AssignableInputOption option
            && (option.Label.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                || option.DeviceDisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
    }

    partial void OnSearchTextChanged(string value) => View.Refresh();

    partial void OnSelectedInputChanged(AssignableInputOption? value) => ConfirmCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm() => Confirmed?.Invoke();

    private bool CanConfirm() => SelectedInput is not null;

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke();
}

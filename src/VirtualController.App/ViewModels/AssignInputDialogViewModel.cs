using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VirtualController.App.ViewModels;

/// <summary>
/// ViewModel for the modal "Assign" dialog (<see cref="Views.AssignInputDialog"/>). Displays the physical
/// inputs gathered by the calling <see cref="MappingRowViewModel"/> through
/// <see cref="MappingRowViewModel.BuildAssignableInputs"/>, grouped by device. Users can filter the list
/// through live search before confirming an input as the mapping row's physical source, as an alternative
/// to physically capturing it with the Capture button.
/// </summary>
public sealed partial class AssignInputDialogViewModel : ObservableObject
{
    private readonly List<AssignableInputOption> _allInputs;

    /// <summary>Current search text. Filters <see cref="View"/> as the user types (see
    /// <see cref="OnSearchTextChanged"/>) against both the input display name and device name.</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>Input currently selected in the list. Determines whether <see cref="ConfirmCommand"/> can run;
    /// an empty selection cannot be confirmed.</summary>
    [ObservableProperty]
    private AssignableInputOption? _selectedInput;

    /// <summary>Filtered view of all supplied inputs, grouped by <see cref="AssignableInputOption.DeviceDisplayName"/>
    /// and bound directly to the view's <c>ListBox</c>.</summary>
    public ICollectionView View { get; }

    /// <summary>Raised when the user confirms the current selection through the OK button, Enter, or a
    /// double-click. The dialog code-behind then closes the window with <c>DialogResult = true</c>.</summary>
    public event Action? Confirmed;

    /// <summary>Raised when the user cancels through the Cancel button or Escape. The dialog code-behind
    /// then closes the window with <c>DialogResult = false</c>.</summary>
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

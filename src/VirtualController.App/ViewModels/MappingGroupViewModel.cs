using System.Collections.ObjectModel;
using VirtualController.Core.Mapping;

namespace VirtualController.App.ViewModels;

/// <summary>
/// One group in the mapping table grouped by target type (see <see cref="ModeViewModel.MappingGroups"/>).
/// Contains all mapping rows with the same <see cref="MappingTargetKind"/> and provides a heading for the UI.
/// A group exists in <see cref="ModeViewModel.MappingGroups"/> only while at least one row uses its target type
/// (see <see cref="ModeViewModel.RebuildMappingGroups"/>); empty groups are not shown.
/// </summary>
public sealed class MappingGroupViewModel
{
    /// <summary>Target type for this group, matching <see cref="MappingRowViewModel.SelectedTargetKind"/> for
    /// every row in <see cref="Rows"/>.</summary>
    public MappingTargetKind Kind { get; }

    /// <summary>Group heading, matching the text shown by each row's target-type ComboBox for the same value
    /// (plain Enum.ToString(), with no additional translation) so the heading and dropdown entry clearly refer
    /// to the same target type.</summary>
    public string Header { get; }

    /// <summary>All mapping rows whose <see cref="MappingRowViewModel.SelectedTargetKind"/> equals
    /// <see cref="Kind"/>, in the same order as the underlying <see cref="ModeViewModel.Mappings"/> list.</summary>
    public ObservableCollection<MappingRowViewModel> Rows { get; } = new();

    public MappingGroupViewModel(MappingTargetKind kind, string header)
    {
        Kind = kind;
        Header = header;
    }
}

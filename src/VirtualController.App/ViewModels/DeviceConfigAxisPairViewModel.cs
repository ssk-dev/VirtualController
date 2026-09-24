using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VirtualController.Core.Devices;

namespace VirtualController.App.ViewModels;

/// <summary>
/// Ein zusammengehoeriges Paar aus Positiv- und Negativ-Eintrag derselben physischen Achse, damit die
/// View beide (sowie die zugehoerigen Kalibrierungs-Einstellmoeglichkeiten, die stets nur auf dem
/// Positiv-Eintrag angezeigt werden, siehe <see cref="DeviceConfigInputRowViewModel.IsCalibratable"/>)
/// direkt untereinander in einem gemeinsamen Rahmen darstellen kann, statt sie - wie zuvor - lose
/// hintereinander in einer flachen Liste zu zeigen. Bietet zusaetzlich ueber <see cref="Enabled"/> einen
/// gemeinsamen Enable/Disable-Schalter fuer beide Richtungen zugleich, damit der Nutzer nicht mehr
/// zwingend jede Richtung einzeln (de-)aktivieren muss, um eine komplette Achse stillzulegen.
/// </summary>
public sealed partial class DeviceConfigAxisPairViewModel : ObservableObject, IDeviceConfigAxisItem
{
    private DeviceConfigInputRowViewModel _positive = null!;
    private DeviceConfigInputRowViewModel? _negative;

    /// <summary>Kanonischer Eintrag der Achse (fuer Mapping-Zwecke massgeblich) - stets vorhanden.</summary>
    public DeviceConfigInputRowViewModel Positive
    {
        get => _positive;
        internal set
        {
            if (_positive is not null)
            {
                _positive.PropertyChanged -= OnRowPropertyChanged;
            }

            _positive = value;
            _positive.PropertyChanged += OnRowPropertyChanged;
        }
    }

    /// <summary>Gegenlaeufiger Eintrag derselben Achse. Bei physisch einseitigen Achsen (Trigger,
    /// Schieberegler) existiert kein Negativ-Eintrag; die View blendet die entsprechende Zeile dann aus.</summary>
    public DeviceConfigInputRowViewModel? Negative
    {
        get => _negative;
        internal set
        {
            if (_negative is not null)
            {
                _negative.PropertyChanged -= OnRowPropertyChanged;
            }

            _negative = value;

            if (_negative is not null)
            {
                _negative.PropertyChanged += OnRowPropertyChanged;
            }

            OnPropertyChanged(nameof(HasNegative));
        }
    }

    /// <summary>true, wenn diese Achse sowohl einen Positiv- als auch einen Negativ-Eintrag besitzt
    /// (zentrierte Sticks/Rotationsachsen); false fuer einseitige Trigger/Schieberegler.</summary>
    public bool HasNegative => Negative is not null;

    /// <summary>Eingebettete Live-Visualisierung dieser Achse (horizontaler Slider, siehe
    /// <see cref="Views.Controls.AxisGaugeControl"/>), direkt in dieser Karte statt in einem separaten
    /// globalen "Live-Vorschau"-Abschnitt dargestellt. Null, bis <see cref="BuildVisualization"/>
    /// aufgerufen wurde (erst moeglich, sobald <see cref="Negative"/> final zugewiesen ist, da dies den
    /// bidirektionalen Wertebereich bestimmt).</summary>
    public AxisVisualizationViewModel? Visualization { get; private set; }

    /// <summary>true, wenn diese Achse Teil eines kombinierten Sticks ist (siehe
    /// <see cref="DeviceConfigStickGroupViewModel"/>), dessen 2D-Pad die Werte beider Achsen bereits
    /// gemeinsam darstellt - die eigene Einzelachsen-Anzeige wuerde den Wert dann redundant ein zweites
    /// Mal (als separater Balken) zeigen und wird deshalb hier ausgeblendet, obwohl <see cref="Visualization"/>
    /// selbst weiterhin existiert und live aktualisiert wird (sie liefert die Rohdaten fuer das 2D-Pad).</summary>
    public bool SuppressOwnVisualizationDisplay { get; set; }

    public bool HasVisualization => Visualization is not null && !SuppressOwnVisualizationDisplay;

    /// <summary>true, wenn diese Achse Teil eines kombinierten Sticks ist (siehe
    /// <see cref="DeviceConfigStickGroupViewModel"/>), der im eigenen Kopfbereich bereits einen einzigen
    /// Schalter fuer beide Achsen zugleich anbietet - der zusaetzliche, achseneigene Master-Schalter waere
    /// dann redundant und wird deshalb ausgeblendet. Fuer eigenstaendige Achsen (Trigger, Schieberegler,
    /// Rotationsachsen ohne Partner) bleibt <see cref="ShowMasterToggle"/> hingegen true, damit auch dort
    /// eine komplette Achse ueber einen einzigen Schalter (de-)aktiviert werden kann, statt zwingend beide
    /// Richtungen einzeln abhaken zu muessen.</summary>
    public bool SuppressMasterToggle { get; set; }

    public bool ShowMasterToggle => !SuppressMasterToggle;

    /// <summary>Erzeugt die eingebettete Live-Visualisierung anhand der bereits vorhandenen
    /// <see cref="InputSettings"/> des Positiv-Eintrags. Wird von <see cref="DeviceConfigDeviceViewModel"/>
    /// erst aufgerufen, nachdem sowohl <see cref="Positive"/> als auch ein etwaiger <see cref="Negative"/>
    /// zugewiesen wurden, damit <see cref="HasNegative"/> zuverlaessig den korrekten Wertebereich
    /// (bidirektional vs. einseitig) liefert. Name bleibt bewusst leer, da die Karte den (editierbaren)
    /// Namen bereits selbst im Kopfbereich anzeigt.</summary>
    public void BuildVisualization()
    {
        Visualization = new AxisVisualizationViewModel(string.Empty, Positive.Settings, Positive.Ref.Index, HasNegative);
        OnPropertyChanged(nameof(HasVisualization));
    }

    public IEnumerable<DeviceConfigInputRowViewModel> AllRows
        => Negative is null ? new[] { Positive } : new[] { Positive, Negative };

    public void UpdateVisualization(DeviceState state) => Visualization?.UpdateFromState(state);

    public void ResetVisualization() => Visualization?.Reset();

    /// <summary>Tri-State-Ausleseweg fuer den gemeinsamen Master-Schalter im Kopfbereich: true, wenn beide
    /// Richtungen aktiv sind, false, wenn beide deaktiviert sind, und null (unbestimmt), wenn sich die
    /// beiden Richtungen unterscheiden - z.B. wenn der Nutzer nur die Negativ-Richtung einzeln deaktiviert
    /// hat. Eine rein binaere Darstellung wuerde in diesem Mischfall faelschlich "komplett deaktiviert"
    /// suggerieren, obwohl die andere Richtung weiterhin unveraendert aktiv bleibt. Nur zum Anzeigen
    /// gedacht (ueber eine <see cref="System.Windows.Controls.CheckBox"/> mit IsThreeState="True" und
    /// IsChecked im OneWay-Modus) - das tatsaechliche Umschalten erfolgt ausschliesslich ueber
    /// <see cref="ToggleEnabledCommand"/>, damit ein einzelner Klick nicht versehentlich im dritten
    /// (unbestimmten) Checkbox-Zustand haengen bleibt.</summary>
    public bool? EnabledState
    {
        get
        {
            var positiveEnabled = Positive.Enabled;
            var negativeEnabled = Negative?.Enabled ?? positiveEnabled;
            return positiveEnabled == negativeEnabled ? positiveEnabled : null;
        }
    }

    /// <summary>Vereinfachter boolescher Ausleseweg fuer IsEnabled-Bindungen (z.B. auf die eingebettete
    /// Visualisierung), die - anders als eine <see cref="System.Windows.Controls.CheckBox"/> - keinen
    /// Tri-State-Wert entgegennehmen koennen: true, solange mindestens eine Richtung aktiv ist (deckt auch
    /// den Mischfall ab), false nur, wenn beide Richtungen vollstaendig deaktiviert sind.</summary>
    public bool IsAnyEnabled => EnabledState != false;

    /// <summary>Setzt beim Anklicken der Master-Checkbox stets beide Richtungen explizit auf denselben
    /// Wert: liegt aktuell kein einheitlicher "beide aktiv"-Zustand vor (also false oder unbestimmt/gemischt),
    /// werden beide Richtungen eingeschaltet; waren zuvor bereits beide aktiv, werden beide ausgeschaltet.</summary>
    [RelayCommand]
    private void ToggleEnabled()
    {
        var newValue = EnabledState != true;
        Positive.Enabled = newValue;
        if (Negative is not null)
        {
            Negative.Enabled = newValue;
        }
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DeviceConfigInputRowViewModel.Enabled))
        {
            OnPropertyChanged(nameof(EnabledState));
            OnPropertyChanged(nameof(IsAnyEnabled));
        }
    }
}

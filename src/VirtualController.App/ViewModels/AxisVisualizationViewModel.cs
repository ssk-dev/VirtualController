using CommunityToolkit.Mvvm.ComponentModel;
using VirtualController.Core.Devices;

namespace VirtualController.App.ViewModels;

/// <summary>
/// Rein darstellendes Modell des aktuellen Live-Eingangswerts einer einzelnen physischen Achse fuer
/// die generische Achsenvisualisierung im Geraete-Konfigurationsdialog (siehe
/// <see cref="Views.Controls.AxisGaugeControl"/>). Enthaelt bewusst keine eigene Logik zur Erzeugung
/// oder Veraenderung von Controllerwerten: die Kalibrierung (<see cref="AxisSignalProcessor.Calibrate"/>)
/// ist rein lesend gegenueber den bereits vorhandenen <see cref="InputSettings"/> und dem zuletzt
/// gepollten <see cref="DeviceState"/>; die Deadzone-Anzeige spiegelt lediglich den dort konfigurierten
/// Wert wider.
/// </summary>
public sealed partial class AxisVisualizationViewModel : ObservableObject, IAxisVisualizationItem
{
    private readonly InputSettings _settings;
    private readonly int _axisSlotIndex;
    private readonly bool _bidirectional;

    /// <summary>Anzeigename der Achse (ohne Richtungssuffix "+"/"-", da diese Visualisierung stets
    /// beide Richtungen derselben physischen Achse gemeinsam als einen Wert darstellt).</summary>
    public string Name { get; }

    /// <summary>Unterer Rand des vollstaendigen Wertebereichs dieser Achse: -1.0 fuer zentrierte
    /// Sticks/Rotationsachsen, 0.0 fuer einseitige Trigger/Schieberegler.</summary>
    public float MinValue => _bidirectional ? -1f : 0f;

    /// <summary>Oberer Rand des vollstaendigen Wertebereichs dieser Achse (stets 1.0).</summary>
    public float MaxValue => 1f;

    /// <summary>true fuer zentrierte Sticks/Rotationsachsen (-1.0 .. 1.0, Nullpunkt liegt in der Mitte),
    /// false fuer einseitige Trigger/Schieberegler (0.0 .. 1.0, Nullpunkt liegt am Rand). Steuert in der
    /// View, ob eine zusaetzliche Nullpunkt-Markierung in der Mitte des Balkens sinnvoll ist.</summary>
    public bool IsBidirectional => _bidirectional;

    /// <summary>Aktueller, kalibrierter Live-Wert (siehe <see cref="AxisSignalProcessor.Calibrate"/>),
    /// bewusst ohne Deadzone-/Kurven-Anwendung, damit sich der angezeigte Marker fluessig durch die
    /// Deadzone hindurch bewegt - die Deadzone selbst wird separat visualisiert.</summary>
    [ObservableProperty]
    private float _value;

    /// <summary>Aktuell konfigurierte geraeteweite Deadzone dieser Achse (siehe <see cref="InputSettings.Deadzone"/>).</summary>
    [ObservableProperty]
    private float _deadzone;

    /// <summary>true, sobald der aktuelle Wert die konfigurierte Deadzone verlaesst. Wird von
    /// <see cref="Views.Controls.AxisGaugeControl"/> genutzt, um beim Uebergang von innerhalb nach
    /// ausserhalb kurz aufzuleuchten.</summary>
    [ObservableProperty]
    private bool _isOutsideDeadzone;

    /// <param name="name">Anzeigename ohne Richtungssuffix.</param>
    /// <param name="settings">Geraeteweite Einstellungen (Kalibrierung/Deadzone) der kanonischen AxisPositive-Eingabe.</param>
    /// <param name="axisSlotIndex">Generischer Achsen-Slot-Index (siehe <see cref="PhysicalAxisId"/>), zum Auslesen aus <see cref="DeviceState.Axes"/>.</param>
    /// <param name="bidirectional">true fuer zentrierte Sticks/Rotationsachsen (-1.0 .. 1.0), false fuer Trigger/Schieberegler (0.0 .. 1.0).</param>
    public AxisVisualizationViewModel(string name, InputSettings settings, int axisSlotIndex, bool bidirectional)
    {
        Name = name;
        _settings = settings;
        _axisSlotIndex = axisSlotIndex;
        _bidirectional = bidirectional;
        _deadzone = settings.Deadzone;
    }

    private float Range => MaxValue - MinValue;

    private static double Clamp01(float v) => Math.Clamp(v, 0f, 1f);

    /// <summary>Position des aktuellen Werts als Anteil (0.0 = <see cref="MinValue"/>, 1.0 = <see cref="MaxValue"/>)
    /// der gesamten Achsenbreite. Dient als Star-Gewicht der Spalte "vor dem Marker" in der View, damit der
    /// Marker unabhaengig von der tatsaechlichen Pixelbreite des Steuerelements proportional positioniert wird.</summary>
    public double MarkerFraction => Clamp01((Value - MinValue) / Range);

    /// <summary>Verbleibender Anteil nach dem Marker (1.0 - <see cref="MarkerFraction"/>), als Star-Gewicht der
    /// dritten (rechten) Spalte.</summary>
    public double AfterMarkerFraction => 1.0 - MarkerFraction;

    /// <summary>Anteilige Position, an der das Deadzone-Band beginnt (linke Kante), unter Beruecksichtigung
    /// des vollstaendigen Wertebereichs (z.B. bei einseitigen Triggern 0..1 kann die Deadzone nicht unter 0 reichen).</summary>
    public double DeadzoneStartFraction => Clamp01((MathF.Max(MinValue, -Deadzone) - MinValue) / Range);

    /// <summary>Anteilige Position, an der das Deadzone-Band endet (rechte Kante).</summary>
    private double DeadzoneEndFraction => Clamp01((MathF.Min(MaxValue, Deadzone) - MinValue) / Range);

    /// <summary>Breite des Deadzone-Bands als Anteil der gesamten Achsenbreite - waechst proportional mit dem
    /// konfigurierten Deadzone-Wert, wie in der Anforderung ("je groesser die Deadzone, desto groesser der
    /// dargestellte Bereich") gefordert.</summary>
    public double DeadzoneWidthFraction => Math.Max(0.0, DeadzoneEndFraction - DeadzoneStartFraction);

    /// <summary>Verbleibender Anteil nach dem Deadzone-Band, als Star-Gewicht der dritten Spalte.</summary>
    public double AfterDeadzoneFraction => 1.0 - DeadzoneStartFraction - DeadzoneWidthFraction;

    partial void OnValueChanged(float value)
    {
        OnPropertyChanged(nameof(MarkerFraction));
        OnPropertyChanged(nameof(AfterMarkerFraction));
    }

    partial void OnDeadzoneChanged(float value)
    {
        OnPropertyChanged(nameof(DeadzoneStartFraction));
        OnPropertyChanged(nameof(DeadzoneWidthFraction));
        OnPropertyChanged(nameof(AfterDeadzoneFraction));
    }

    /// <summary>Aktualisiert Wert und Deadzone-Zustand anhand des zuletzt gepollten Geraetezustands.
    /// Wird vom Live-Polling der uebergeordneten <see cref="DeviceConfigDeviceViewModel"/> aufgerufen.</summary>
    public void UpdateFromState(DeviceState state)
    {
        float raw = state.GetAxisRaw(_axisSlotIndex);
        Value = AxisSignalProcessor.Calibrate(raw, _settings, _bidirectional);
        // Deadzone wird bei jedem Tick frisch aus den Einstellungen gelesen, damit eine waehrend des
        // geoeffneten Dialogs vom Nutzer angepasste Deadzone sofort in der Visualisierung sichtbar wird.
        Deadzone = _settings.Deadzone;
        IsOutsideDeadzone = MathF.Abs(Value) > Deadzone;
    }

    /// <summary>Setzt die Anzeige auf den Ruhezustand zurueck (z.B. beim Zuklappen des Geraets im
    /// Konfigurationsdialog oder Trennen der Verbindung), damit keine veraltete Position stehen bleibt.</summary>
    public void Reset()
    {
        Value = 0f;
        IsOutsideDeadzone = false;
    }
}

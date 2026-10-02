using VirtualController.Core.Devices;
using VirtualController.Core.Virtual;

namespace VirtualController.Core.Mapping;

/// <summary>
/// Berechnet aus den aktuellen Zustaenden aller zugeordneten physischen Geraete den
/// resultierenden Zustand eines virtuellen Controllers, gemaess dessen Mapping-Tabelle.
/// Wird bei jedem Tick des Polling-Loops einmal pro virtuellem Controller aufgerufen.
/// Zustandslos / threadsicher, solange die uebergebenen Dictionaries waehrend des Aufrufs
/// nicht von einem anderen Thread veraendert werden.
/// </summary>
public static class MappingEngine
{
    /// <summary>
    /// Wendet alle Mapping-Eintraege eines Profils auf die zuletzt gelesenen Device-States an
    /// und schreibt das Ergebnis in <paramref name="target"/> (wird vorher zurueckgesetzt).
    /// </summary>
    /// <param name="profile">Das Profil des virtuellen Controllers.</param>
    /// <param name="latestStates">Zuletzt gepollter Zustand je physischem Geraet (Key = DeviceId).</param>
    /// <param name="target">Wiederverwendbarer Ziel-Zustand, wird in-place aktualisiert.</param>
    /// <param name="deviceSettings">Optionale, geraeteweite Einstellungen (Key = DeviceId). Wird genutzt, um
    /// einzelne, vom Nutzer im Konfigurationsdialog deaktivierte physische Eingaben (z.B. ein schwammiger,
    /// verschlissener Button) unabhaengig von der Mapping-Tabelle vollstaendig zu ignorieren. Null bedeutet
    /// "keine Einstellungen vorhanden" -> alle Eingaben gelten als aktiviert (Standardverhalten).</param>
    public static void Apply(
        VirtualControllerProfile profile,
        IReadOnlyDictionary<string, DeviceState> latestStates,
        VirtualPadState target,
        IReadOnlyDictionary<string, DeviceSettings>? deviceSettings = null)
    {
        target.Reset();

        bool dpadUp = false, dpadDown = false, dpadLeft = false, dpadRight = false;

        // Nur die Mapping-Tabelle des aktuell aktiven Modus wird ausgewertet (siehe VirtualControllerProfile.ActiveMode);
        // ohne aktiven Modus (z.B. noch keiner angelegt) bleibt target auf dem oben gesetzten Reset-Zustand.
        var activeMappings = profile.ActiveMode?.Mappings;
        if (activeMappings is null)
        {
            return;
        }

        foreach (var entry in activeMappings)
        {
            if (!latestStates.TryGetValue(entry.SourceDeviceId, out var state))
            {
                continue; // Quellgeraet aktuell nicht verbunden -> Eintrag wird einfach ignoriert.
            }

            if (!deviceSettings.IsInputEnabled(entry.SourceDeviceId, entry.SourceKind, entry.SourceIndex))
            {
                continue; // Physische Eingabe wurde im Konfigurationsdialog deaktiviert -> ignorieren.
            }

            switch (entry.SourceKind)
            {
                case PhysicalInputKind.Button:
                    ApplyDigitalSource(entry, IsButtonPressed(state, entry.SourceIndex), target,
                        ref dpadUp, ref dpadDown, ref dpadLeft, ref dpadRight);
                    break;

                case PhysicalInputKind.AxisPositive:
                case PhysicalInputKind.AxisNegative:
                    ApplyAxisSource(entry, state, target, deviceSettings,
                        ref dpadUp, ref dpadDown, ref dpadLeft, ref dpadRight);
                    break;

                case PhysicalInputKind.DPad:
                    ApplyPovSource(entry, state, target,
                        ref dpadUp, ref dpadDown, ref dpadLeft, ref dpadRight);
                    break;

                case PhysicalInputKind.DPadUp:
                case PhysicalInputKind.DPadDown:
                case PhysicalInputKind.DPadLeft:
                case PhysicalInputKind.DPadRight:
                    ApplyPovDirectionSource(entry, state, target,
                        ref dpadUp, ref dpadDown, ref dpadLeft, ref dpadRight);
                    break;
            }
        }

        // DPad wird zum Schluss aus den waehrend der Schleife gesammelten Flags kombiniert,
        // damit z.B. "Up" und "Right" von zwei unterschiedlichen Mapping-Eintraegen stammen
        // und trotzdem korrekt zu "UpRight" zusammengefuehrt werden.
        if (target.DPad == DPadDirection.None)
        {
            target.DPad = DPadDirectionExtensions.FromFlags(dpadUp, dpadDown, dpadLeft, dpadRight);
        }
    }

    /// <summary>
    /// Ermittelt, ob eine physische Eingabe (referenziert ueber <see cref="PhysicalInputTrigger"/>, z.B.
    /// ein Modus-Umschalt-Ausloeser) im aktuellen Tick "aktiv" ist (Button gedrueckt, Achse ueber ihrer
    /// Deadzone ausgeschlagen, D-Pad-Richtung aktiv). Im Gegensatz zu den <see cref="MappingEntry"/>-
    /// bezogenen Auswertungen oben gibt es hier kein Ziel und keine pro-Eintrag konfigurierte Deadzone -
    /// bei Achsen wird daher die geraeteweite Kalibrierung (falls vorhanden) bzw. der Standardwert
    /// verwendet (siehe <see cref="Devices.DeviceSettingsExtensions.ResolveDefaultAxisDeadzone"/>). Wird
    /// von <see cref="Engine.ControllerSession"/> fuer die Flankenerkennung von Toggle-/Switch-Triggern
    /// verwendet, unabhaengig vom aktuell aktiven Modus.
    /// </summary>
    public static bool IsPhysicalInputActive(
        PhysicalInputTrigger trigger,
        IReadOnlyDictionary<string, DeviceState> latestStates,
        IReadOnlyDictionary<string, DeviceSettings>? deviceSettings = null)
    {
        if (!latestStates.TryGetValue(trigger.DeviceId, out var state))
        {
            return false;
        }

        switch (trigger.Kind)
        {
            case PhysicalInputKind.Button:
                return IsButtonPressed(state, trigger.Index);

            case PhysicalInputKind.AxisPositive:
            case PhysicalInputKind.AxisNegative:
                bool isXInputSource = trigger.DeviceId.StartsWith("xinput:", StringComparison.Ordinal);
                bool isTriggerLikeSlot = isXInputSource
                    ? trigger.Index is (int)PhysicalAxisId.RotationY or (int)PhysicalAxisId.RotationZ
                    : trigger.Index is (int)PhysicalAxisId.Slider0 or (int)PhysicalAxisId.Slider1;
                var axisSettings = deviceSettings.TryGetInputSettings(trigger.DeviceId, PhysicalInputKind.AxisPositive, trigger.Index);
                float raw = AxisSignalProcessor.Process(state.GetAxisRaw(trigger.Index), axisSettings, bidirectional: !isTriggerLikeSlot);
                float magnitude = trigger.Kind == PhysicalInputKind.AxisPositive ? MathF.Max(raw, 0f) : MathF.Max(-raw, 0f);
                float deadzone = deviceSettings.ResolveDefaultAxisDeadzone(trigger.DeviceId, trigger.Index);
                return magnitude > deadzone;

            case PhysicalInputKind.DPad:
                return DPadDirectionExtensions.FromPovDegrees(state.PovDirectionDegrees) != DPadDirection.None;

            case PhysicalInputKind.DPadUp:
            case PhysicalInputKind.DPadDown:
            case PhysicalInputKind.DPadLeft:
            case PhysicalInputKind.DPadRight:
                var direction = DPadDirectionExtensions.FromPovDegrees(state.PovDirectionDegrees);
                return trigger.Kind switch
                {
                    PhysicalInputKind.DPadUp => direction.HasUp(),
                    PhysicalInputKind.DPadDown => direction.HasDown(),
                    PhysicalInputKind.DPadLeft => direction.HasLeft(),
                    PhysicalInputKind.DPadRight => direction.HasRight(),
                    _ => false
                };

            default:
                return false;
        }
    }

    private static bool IsButtonPressed(DeviceState state, int index)
        => index >= 0 && index < state.Buttons.Length && state.Buttons[index];

    private static void ApplyDigitalSource(
        MappingEntry entry, bool pressed, VirtualPadState target,
        ref bool dpadUp, ref bool dpadDown, ref bool dpadLeft, ref bool dpadRight)
    {
        if (!pressed)
        {
            return;
        }

        switch (entry.TargetKind)
        {
            case MappingTargetKind.Button when entry.TargetButton.HasValue:
                target.PressedButtons.Add(entry.TargetButton.Value);
                break;

            case MappingTargetKind.Trigger when entry.TargetTrigger.HasValue:
                SetTrigger(target, entry.TargetTrigger.Value, 1f);
                break;

            case MappingTargetKind.Axis when entry.TargetAxis.HasValue:
                // Digitaler Button auf eine Achse gemappt (z.B. Buttons als Stick-Ersatz) -> voller Ausschlag.
                SetAxis(target, entry.TargetAxis.Value, entry.Invert ? -1f : 1f);
                break;

            case MappingTargetKind.DPad when entry.TargetDPadDirection.HasValue:
                AccumulateDPad(entry.TargetDPadDirection.Value, ref dpadUp, ref dpadDown, ref dpadLeft, ref dpadRight);
                break;
        }
    }

    private static void ApplyAxisSource(
        MappingEntry entry, DeviceState state, VirtualPadState target,
        IReadOnlyDictionary<string, DeviceSettings>? deviceSettings,
        ref bool dpadUp, ref bool dpadDown, ref bool dpadLeft, ref bool dpadRight)
    {
        // Eine physische Achse besitzt nur eine Kalibrierung/Deadzone/Kurve, obwohl sie im Katalog als
        // zwei getrennte Eintraege (AxisPositive/AxisNegative) auftritt (je einer pro Ausschlagsrichtung
        // fuer die Mapping-Zuordnung) -> Achseneinstellungen werden stets ueber den kanonischen
        // AxisPositive-Schluessel derselben Achsen-Nummer nachgeschlagen, unabhaengig von der tatsaechlichen
        // Richtung dieses konkreten Mapping-Eintrags (Enabled/Umbenennung bleiben davon unberuehrt und
        // werden weiterhin ueber den jeweils eigenen Schluessel der tatsaechlichen Richtung ausgewertet,
        // siehe Aufrufer). Welche Slots physisch einseitig (0..1, "trigger-artig") statt zentriert/
        // bidirektional (-1..1) sind, haengt von der Quell-API ab: Bei XInput liegen die Trigger auf
        // Slot 4/5 (RotationY/RotationZ, siehe XInputDeviceReader.FixedAvailableAxes), waehrend genau
        // diese Slot-Nummern bei DirectInput echte, bidirektionale Rotationsachsen sind - dort sind
        // stattdessen Slot 6/7 (Slider0/Slider1) die physisch einseitigen Schieberegler. Ein reiner
        // Index-Vergleich ohne API-Unterscheidung wuerde daher fuer eine der beiden APIs falsch liegen.
        bool isXInputSource = entry.SourceDeviceId.StartsWith("xinput:", StringComparison.Ordinal);
        bool isTriggerLikeSlot = isXInputSource
            ? entry.SourceIndex is (int)PhysicalAxisId.RotationY or (int)PhysicalAxisId.RotationZ
            : entry.SourceIndex is (int)PhysicalAxisId.Slider0 or (int)PhysicalAxisId.Slider1;
        var axisSettings = deviceSettings.TryGetInputSettings(entry.SourceDeviceId, PhysicalInputKind.AxisPositive, entry.SourceIndex);
        float raw = AxisSignalProcessor.Process(state.GetAxisRaw(entry.SourceIndex), axisSettings, bidirectional: !isTriggerLikeSlot);
        bool wantPositive = entry.SourceKind == PhysicalInputKind.AxisPositive;

        // Fuer Digital-Ziele (Button/DPad/Trigger) wird die Achse als Schwellwert-Schalter behandelt.
        // Die geraeteweite Deadzone (siehe AxisSignalProcessor.Process) hat Werte innerhalb ihres Radius
        // bereits auf exakt 0 gesetzt, ein einfacher > 0-Vergleich reicht daher aus.
        float magnitude = wantPositive ? MathF.Max(raw, 0f) : MathF.Max(-raw, 0f);
        bool digitalPressed = magnitude > 0f;

        // Analog zu "magnitude", aber VORZEICHENERHALTEND statt auf 0..1 normalisiert: wird fuer
        // DirectionalOnly bei Achsen-Zielen benoetigt (siehe unten) - "magnitude" eignet sich dort
        // NICHT, da ihr Vorzeichen immer positiv ist und damit die isolierte Achsenhaelfte faelschlich
        // stets Richtung positiv auf der virtuellen Achse ausschlagen wuerde, unabhaengig davon, ob
        // SourceKind tatsaechlich AxisPositive oder AxisNegative ist.
        float directionalSignedValue = wantPositive ? MathF.Max(raw, 0f) : MathF.Min(raw, 0f);

        switch (entry.TargetKind)
        {
            case MappingTargetKind.Axis when entry.TargetAxis.HasValue:
                // DirectionalOnly: nur die durch SourceKind festgelegte Haelfte der physischen Achse
                // (als vorzeichenerhaltendes "directionalSignedValue" isoliert) verwenden, statt wie im
                // Standardfall den vollen bidirektionalen Rohwert durchzureichen. So koennen zwei
                // unabhaengige physische Achsenhaelften (z.B. Y+ und X+) mit jeweils eigenem Invert-
                // Vorzeichen auf dieselbe oder unterschiedliche virtuelle Achsen aufgeteilt werden. Die
                // geraeteweite Deadzone (inkl. Neuskalierung ab der Deadzone-Grenze) ist bereits ueber
                // AxisSignalProcessor.Process in "raw" enthalten.
                float normalized = entry.DirectionalOnly ? directionalSignedValue : raw;
                if (entry.Invert) normalized = -normalized;
                SetAxis(target, entry.TargetAxis.Value, normalized);
                break;

            case MappingTargetKind.Trigger when entry.TargetTrigger.HasValue:
                // Der jeweils trigger-artige Slot ist bereits vom zustaendigen Reader auf 0..1 normalisiert
                // (XInput-Trigger bzw. DirectInput-Schieberegler, siehe API-abhaengige Ermittlung von
                // isTriggerLikeSlot oben) und hat die geraeteweite Deadzone bereits ueber
                // AxisSignalProcessor.Process durchlaufen. Alle anderen Achsen (z.B. ein Stick als
                // Trigger-Ersatz gemappt) nutzen stattdessen den Magnitude-Anteil.
                float triggerValue = isTriggerLikeSlot ? raw : magnitude;
                SetTrigger(target, entry.TargetTrigger.Value, Math.Clamp(triggerValue, 0f, 1f));
                break;

            case MappingTargetKind.Button when entry.TargetButton.HasValue && digitalPressed:
                target.PressedButtons.Add(entry.TargetButton.Value);
                break;

            case MappingTargetKind.DPad when entry.TargetDPadDirection.HasValue && digitalPressed:
                AccumulateDPad(entry.TargetDPadDirection.Value, ref dpadUp, ref dpadDown, ref dpadLeft, ref dpadRight);
                break;
        }
    }

    private static void ApplyPovDirectionSource(
        MappingEntry entry, DeviceState state, VirtualPadState target,
        ref bool dpadUp, ref bool dpadDown, ref bool dpadLeft, ref bool dpadRight)
    {
        var direction = DPadDirectionExtensions.FromPovDegrees(state.PovDirectionDegrees);
        bool pressed = entry.SourceKind switch
        {
            PhysicalInputKind.DPadUp => direction.HasUp(),
            PhysicalInputKind.DPadDown => direction.HasDown(),
            PhysicalInputKind.DPadLeft => direction.HasLeft(),
            PhysicalInputKind.DPadRight => direction.HasRight(),
            _ => false
        };

        ApplyDigitalSource(entry, pressed, target, ref dpadUp, ref dpadDown, ref dpadLeft, ref dpadRight);
    }

    private static void ApplyPovSource(
        MappingEntry entry, DeviceState state, VirtualPadState target,
        ref bool dpadUp, ref bool dpadDown, ref bool dpadLeft, ref bool dpadRight)
    {
        var direction = DPadDirectionExtensions.FromPovDegrees(state.PovDirectionDegrees);
        if (direction == DPadDirection.None)
        {
            return;
        }

        switch (entry.TargetKind)
        {
            case MappingTargetKind.DPad:
                target.DPad = direction;
                break;

            case MappingTargetKind.Button when entry.TargetButton.HasValue:
                target.PressedButtons.Add(entry.TargetButton.Value);
                break;
        }
    }

    private static void AccumulateDPad(DPadDirection direction, ref bool up, ref bool down, ref bool left, ref bool right)
    {
        up |= direction.HasUp();
        down |= direction.HasDown();
        left |= direction.HasLeft();
        right |= direction.HasRight();
    }

    private static void SetAxis(VirtualPadState target, VirtualAxis axis, float value)
    {
        switch (axis)
        {
            case VirtualAxis.LeftStickX: target.LeftStickX = value; break;
            case VirtualAxis.LeftStickY: target.LeftStickY = value; break;
            case VirtualAxis.RightStickX: target.RightStickX = value; break;
            case VirtualAxis.RightStickY: target.RightStickY = value; break;
        }
    }

    private static void SetTrigger(VirtualPadState target, VirtualTrigger trigger, float value)
    {
        switch (trigger)
        {
            case VirtualTrigger.LeftTrigger: target.LeftTrigger = value; break;
            case VirtualTrigger.RightTrigger: target.RightTrigger = value; break;
        }
    }
}

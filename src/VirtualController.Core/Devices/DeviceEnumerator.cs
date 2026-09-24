using Vortice.DirectInput;

namespace VirtualController.Core.Devices;

/// <summary>
/// Findet alle aktuell angeschlossenen physischen Controller ueber XInput (Slots 0-3) und
/// DirectInput (alle uebrigen HID-Gamepads/Joysticks, inkl. der meisten PlayStation-Controller).
/// Geraete, die bereits ueber XInput gefunden wurden, werden bei der DirectInput-Enumeration
/// nicht doppelt aufgelistet (XInput-Controller melden sich auch als DirectInput-Geraet).
/// </summary>
public static class DeviceEnumerator
{
    // TEMPORAERES DEBUG-LOGGING fuer die Achsen-Diagnose: Core kennt DebugLog (App-Projekt) nicht,
    // daher hier ein simpler Hook, den die App-Schicht beim Start setzt. Bitte nach Abschluss der
    // Diagnose wieder entfernen.
    public static Action<string>? OnDebugLog;

    /// <summary>Einmal ermittelte Faehigkeiten eines DirectInput-Geraets, gecacht ueber die gesamte
    /// Prozesslaufzeit (siehe <see cref="_capabilitiesCache"/> und dessen Nutzung in <see cref="EnumerateAll"/>).</summary>
    private sealed record DirectInputCapabilities(int ButtonCount, bool HasPov, List<PhysicalAxisId> AvailableAxes);

    /// <summary>
    /// Cache der Faehigkeiten (ButtonCount/HasPov/AvailableAxes) bereits erkannter DirectInput-Geraete,
    /// Key = <see cref="DeviceInstance.InstanceGuid"/>. <see cref="EnumerateAll"/> wird periodisch alle
    /// 2 Sekunden vom Hotplug-Timer aufgerufen (siehe <c>MainViewModel.HotplugPollInterval</c>); ohne
    /// diesen Cache wuerde dabei fuer JEDES angeschlossene Geraet bei JEDEM Scan erneut ein
    /// <see cref="Vortice.DirectInput.IDirectInputDevice8"/> per <c>CreateDevice()</c> erzeugt und
    /// <c>Capabilities</c>/<c>GetObjects(Axis)</c> abgefragt - auch fuer Geraete, die seit dem letzten
    /// Scan unveraendert angeschlossen sind. Das ist unnoetiger COM-Overhead und erzeugt nebenbei ein
    /// zusaetzliches kurzlebiges Device-Objekt fuer ein Geraet, das ggf. gerade von einem laufenden
    /// <see cref="DirectInputDeviceReader"/> aktiv gepollt wird. Die Faehigkeiten eines physischen
    /// Geraets aendern sich waehrend es angeschlossen ist nicht, daher ist prozessweites Caching sicher.
    /// </summary>
    private static readonly Dictionary<Guid, DirectInputCapabilities> _capabilitiesCache = new();

    public static IReadOnlyList<PhysicalDeviceInfo> EnumerateAll()
    {
        var result = new List<PhysicalDeviceInfo>();
        var xinputSlots = new HashSet<int>();

        for (int i = 0; i < 4; i++)
        {
            if (!XInputDeviceReader.IsConnected(i))
            {
                continue;
            }

            xinputSlots.Add(i);
            result.Add(new PhysicalDeviceInfo(
                DeviceId: $"xinput:{i}",
                DisplayName: $"XInput Controller {i + 1}",
                Api: InputApi.XInput,
                ApiSlot: i,
                ButtonCount: 14,
                HasPov: false));
        }

        using var directInput = DirectInputFactory.Create();
        int diSlot = 0;
        // Sammelt Vendor-/Product-ID-Paare aller DirectInput-Geraete, die als XInput-artig erkannt wurden
        // (siehe LooksLikeXInputDevice) - XInput selbst liefert keine VID/PID, benoetigt fuer die
        // HidHide-Geraetesperre (siehe Zuweisung an die XInput-Eintraege nach dieser Schleife).
        var xinputVidPidCandidates = new HashSet<(ushort VendorId, ushort ProductId)>();
        foreach (var deviceInstance in directInput.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly))
        {
            // Geraete, die XInput bereits liefert, hier nicht zusaetzlich als DirectInput-Duplikat listen.
            // Heuristik: XInput-faehige Geraete erkennt man zuverlaessig nur über den XInput-Slot selbst,
            // daher wird bei aktiven XInput-Slots die Anzahl gleichnamiger DirectInput-Gamepads gedeckelt.
            if (xinputSlots.Count > 0 && LooksLikeXInputDevice(deviceInstance.InstanceName))
            {
                if (TryGetVendorProductId(deviceInstance.ProductGuid, out var xVendorId, out var xProductId))
                {
                    xinputVidPidCandidates.Add((xVendorId, xProductId));
                }
                diSlot++;
                continue;
            }

            // Bereits bekannte, weiterhin angeschlossene Geraete direkt aus dem Cache bedienen -
            // ihre Faehigkeiten aendern sich waehrend der Verbindung nicht, ein erneutes
            // CreateDevice()/Capabilities/GetObjects(Axis) waere bei jedem periodischen Hotplug-Scan
            // unnoetiger COM-Overhead (siehe _capabilitiesCache-Dokumentation).
            if (!_capabilitiesCache.TryGetValue(deviceInstance.InstanceGuid, out var capabilities))
            {
                using var device = directInput.CreateDevice(deviceInstance.InstanceGuid);
                capabilities = new DirectInputCapabilities(
                    ButtonCount: Math.Max(device.Capabilities.ButtonCount, 1),
                    HasPov: device.Capabilities.PovCount > 0,
                    AvailableAxes: DetectAvailableAxes(device));
                _capabilitiesCache[deviceInstance.InstanceGuid] = capabilities;
            }

            bool hasVidPid = TryGetVendorProductId(deviceInstance.ProductGuid, out var vendorId, out var productId);
            result.Add(new PhysicalDeviceInfo(
                DeviceId: $"dinput:{deviceInstance.InstanceGuid}",
                DisplayName: deviceInstance.InstanceName,
                Api: InputApi.DirectInput,
                ApiSlot: diSlot++,
                ButtonCount: capabilities.ButtonCount,
                HasPov: capabilities.HasPov,
                AvailableAxes: capabilities.AvailableAxes,
                VendorId: hasVidPid ? vendorId : null,
                ProductId: hasVidPid ? productId : null));
        }

        // Nur bei genau einem eindeutigen VID/PID-Kandidaten zuweisen: bei mehreren gleichzeitig
        // angeschlossenen, unterschiedlichen Controller-Modellen ist die Zuordnung XInput-Slot ->
        // physisches Geraet nicht zuverlaessig moeglich (XInput selbst bietet dafuer keine API) -
        // eine Fehlzuordnung (falsches Geraet gesperrt) waere riskanter als gar keine Sperre.
        if (xinputVidPidCandidates.Count == 1)
        {
            var (resolvedVendorId, resolvedProductId) = xinputVidPidCandidates.Single();
            for (int i = 0; i < result.Count; i++)
            {
                if (result[i].Api == InputApi.XInput)
                {
                    result[i] = result[i] with { VendorId = resolvedVendorId, ProductId = resolvedProductId };
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Extrahiert Vendor-/Product-ID aus DirectInputs <see cref="DeviceInstance.ProductGuid"/>. DirectInput
    /// kodiert USB-HID-Geraete standardmaessig als GUID der Form "ppppvvvv-0000-0000-0000-504944564944" -
    /// die letzten 6 Bytes buchstabieren in ASCII "PIDVID" (0x50,0x49,0x44,0x56,0x49,0x44), was als
    /// Erkennungsmerkmal fuer dieses feste Layout dient. <see cref="Guid.ToByteArray"/> liefert Data1 in
    /// Little-Endian-Reihenfolge (Bytes 0-3), daher: VendorId = Bytes[0..1], ProductId = Bytes[2..3].
    /// Nur fuer diese HidHide-Geraeteauflösung benoetigt, daher bewusst lokal und nicht ueber Vortice
    /// selbst verfuegbar (dort existiert keine direkte VendorId/ProductId-Property).
    /// </summary>
    private static bool TryGetVendorProductId(Guid productGuid, out ushort vendorId, out ushort productId)
    {
        var bytes = productGuid.ToByteArray();
        // Byte 10-15 muessen ASCII "PIDVID" sein, sonst folgt das Geraet nicht dem erwarteten Standard-Layout.
        ReadOnlySpan<byte> pidVidMarker = "PIDVID"u8;
        if (!bytes.AsSpan(10, 6).SequenceEqual(pidVidMarker))
        {
            vendorId = 0;
            productId = 0;
            return false;
        }

        vendorId = (ushort)(bytes[0] | (bytes[1] << 8));
        productId = (ushort)(bytes[2] | (bytes[3] << 8));
        return true;
    }

    /// <summary>
    /// Ermittelt, welche der generischen Achsen-Slots (siehe <see cref="PhysicalAxisId"/>) ein konkretes
    /// DirectInput-Geraet tatsaechlich besitzt. Dies geschieht ueber die stabile, herstellerunabhaengige
    /// HID-<see cref="DeviceObjectInstance.Usage"/>-ID (Generic-Desktop-Page) jedes von
    /// <see cref="Vortice.DirectInput.IDirectInputDevice8.GetObjects"/> gemeldeten Achsen-Objekts.
    /// WICHTIG: <see cref="DeviceObjectInstance.Offset"/> ist hierfuer NICHT geeignet - DirectInput
    /// vergibt diesen Offset vor einem SetDataFormat()-Aufruf nicht deterministisch (er wird nach der
    /// internen Enumerationsreihenfolge lueckenlos ab 0 gepackt), wodurch Geraete mit Luecken im
    /// Achsen-Layout (z.B. RzAxis + Slider, aber kein RxAxis/RyAxis) falsch zugeordnet wurden - ein
    /// reiner Rohwert-Vergleich ("ist der Wert != 0?") schlaegt ebenfalls fehl, da nicht vorhandene
    /// Achsen ebenfalls einen (falschen) Rohwert liefern.
    /// </summary>
    private static List<PhysicalAxisId> DetectAvailableAxes(Vortice.DirectInput.IDirectInputDevice8 device)
    {
        // HID-Usage-IDs der "Generic Desktop"-Page sind - im Gegensatz zum von DirectInput vor
        // SetDataFormat() nicht-deterministisch (gepackt nach interner Enumerationsreihenfolge)
        // vergebenen Offset - fest und herstellerunabhaengig: 0x30=X, 0x31=Y, 0x32=Z, 0x33=RotationX,
        // 0x34=RotationY, 0x35=RotationZ, 0x36=Slider. Ein reiner Offset-Abgleich schlaegt bei
        // Geraeten mit Luecken im Achsen-Layout (z.B. RzAxis + Slider, aber kein RxAxis/RyAxis)
        // fehl, weil DirectInput die vorhandenen Achsen-Objekte dann lueckenlos ab Offset 0 packt -
        // das fuehrt zu einer falschen Zuordnung (z.B. RzAxis wird faelschlich als RotationX
        // erkannt), wodurch der eigentlich bewegte Wert nie ausgelesen wird, waehrend der falsch
        // zugeordnete Slot (RotationX/RotationY) mangels Treiber-Daten dauerhaft bei Rohwert 0
        // (normalisiert -1.0) verbleibt.
        var usageToAxis = new Dictionary<int, PhysicalAxisId>
        {
            [0x30] = PhysicalAxisId.X,
            [0x31] = PhysicalAxisId.Y,
            [0x32] = PhysicalAxisId.Z,
            [0x33] = PhysicalAxisId.RotationX,
            [0x34] = PhysicalAxisId.RotationY,
            [0x35] = PhysicalAxisId.RotationZ,
            [0x36] = PhysicalAxisId.Slider0,
        };

        var axes = new List<PhysicalAxisId>();
        bool slider0Assigned = false;
        foreach (var objectInfo in device.GetObjects(DeviceObjectTypeFlags.Axis))
        {
            // TEMPORAERES DEBUG-LOGGING: zeigt den rohen DirectInput-Objektnamen und die
            // HID-Usage-ID, damit live verifiziert werden kann, dass die Usage-basierte Zuordnung
            // (im Gegensatz zur vorherigen, instabilen Offset-basierten Zuordnung) bei jedem
            // Enumerate-Aufruf konsistent bleibt. Bitte nach Abschluss der Diagnose wieder entfernen.
            var matched = usageToAxis.TryGetValue(objectInfo.Usage, out var loggedAxisId)
                ? loggedAxisId.ToString()
                : "KEIN MATCH";
            OnDebugLog?.Invoke(
                $"[AxisDetect] Objekt Name='{objectInfo.Name}' Offset={objectInfo.Offset} Usage=0x{objectInfo.Usage:X2} -> {matched}");

            if (!usageToAxis.TryGetValue(objectInfo.Usage, out var axisId))
            {
                continue;
            }

            // Zweiten Slider (falls vorhanden) auf Slider1 statt erneut Slider0 mappen.
            if (axisId == PhysicalAxisId.Slider0 && slider0Assigned)
            {
                axisId = PhysicalAxisId.Slider1;
            }
            else if (axisId == PhysicalAxisId.Slider0)
            {
                slider0Assigned = true;
            }

            if (!axes.Contains(axisId))
            {
                axes.Add(axisId);
            }
        }

        return axes;
    }

    /// <summary>Oeffnet einen konkreten Reader fuer ein zuvor per <see cref="EnumerateAll"/> gefundenes Geraet.</summary>
    public static IDeviceReader OpenReader(PhysicalDeviceInfo info)
    {
        if (info.Api == InputApi.XInput)
        {
            return new XInputDeviceReader(info.ApiSlot);
        }

        var guid = Guid.Parse(info.DeviceId.Substring("dinput:".Length));
        using var directInput = DirectInputFactory.Create();
        var device = directInput.CreateDevice(guid);
        return new DirectInputDeviceReader(device, info, info.ButtonCount);
    }

    private static bool LooksLikeXInputDevice(string instanceName)
        => instanceName.Contains("XInput", StringComparison.OrdinalIgnoreCase)
           || instanceName.Contains("Xbox", StringComparison.OrdinalIgnoreCase);
}

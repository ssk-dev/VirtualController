using System.Text.Json;
using Nefarius.Drivers.HidHide;
using Nefarius.Utilities.DeviceManagement.PnP;

namespace VirtualController.Core.Devices;

/// <summary>
/// Kapselt die Fernsteuerung des separat zu installierenden HidHide-Treibers (https://github.com/nefarius/HidHide)
/// ueber das offizielle <c>Nefarius.Drivers.HidHide</c>-Paket ("classic", nicht-DI-Nutzung). Sperrt die
/// physischen Geraete eines virtuellen Controllers fuer alle anderen Anwendungen, solange dieser virtuelle
/// Controller laeuft (siehe <see cref="Engine.ControllerManager"/>), damit z.B. ein Spiel nicht gleichzeitig
/// auf das physische UND das davon abgeleitete virtuelle Geraet reagiert.
///
/// Referenzgezaehlt: mehrere virtuelle Controller koennen sich theoretisch ein physisches Geraet teilen
/// (z.B. wenn zwei Profile dasselbe Geraet in ihrer Mapping-Tabelle referenzieren) - eine PnP-Instanz-ID
/// wird erst dann tatsaechlich aus der HidHide-Sperrliste entfernt, wenn kein aktiver Controller mehr
/// darauf verweist (siehe <see cref="Lock"/>/<see cref="Unlock"/>).
///
/// <see cref="IHidHideControlService.IsActive"/> (der globale Ein/Aus-Schalter des gesamten Treibers) wird
/// nur dann von dieser Klasse aktiviert, wenn zuvor noch keine eigenen Sperren bestanden, und nur wieder
/// deaktiviert, wenn nach einer Entsperrung keine eigenen Sperren mehr uebrig sind - andere Anwendungen,
/// die IsActive/BlockedInstanceIds bereits unabhaengig davon verwenden, werden dadurch nicht gestoert.
///
/// Absturz-Sicherheit: die eigenen aktuell gesperrten Instanz-IDs werden nach jeder Aenderung in eine kleine
/// Datei persistiert (siehe <see cref="PersistLockState"/>), damit <see cref="CleanupOrphanedLocks"/> beim
/// naechsten App-Start verwaiste Sperren erkennen und entfernen kann, falls die App zuvor abgestuerzt ist
/// (regulaeres Beenden ruft <see cref="Unlock"/> fuer alle Sperren auf, wodurch die Datei wieder leer wird).
/// </summary>
public sealed class HidHideController
{
    private readonly IHidHideControlService _service = new HidHideControlService();

    /// <summary>Referenzzaehlung je gesperrter PnP-Instanz-ID, ueber alle virtuellen Controller hinweg.</summary>
    private readonly Dictionary<string, int> _refCounts = new();

    /// <summary>Ablageort der eigenen, aktuell gesperrten Instanz-IDs - siehe <see cref="PersistLockState"/>/
    /// <see cref="CleanupOrphanedLocks"/>. Bewusst eine eigene, sehr einfache Datei statt Teil von
    /// <see cref="Profiles.ProfileStore"/>: dieser Zustand ist reine Laufzeit-/Absturz-Diagnoseinformation,
    /// kein vom Nutzer editiertes Profil.</summary>
    private static string LockStateFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VirtualController", "hidhide-locks.json");

    /// <summary>Ob der HidHide-Treiber installiert UND betriebsbereit ist. Wenn false, darf die Sperrfunktion
    /// nicht angeboten werden (siehe UI: die zugehoerige Checkbox wird dann ausgegraut). Fragt den Treiber bei
    /// jedem Aufruf frisch ab (kein Caching), da sich der Installationsstatus waehrend der Laufzeit der
    /// Anwendung aendern kann (Nutzer installiert HidHide nachtraeglich).</summary>
    public bool IsAvailable
    {
        get
        {
            try
            {
                return _service.IsInstalled && _service.IsOperational;
            }
            catch
            {
                // Treiber-Abfrage kann fehlschlagen, wenn HidHide ueberhaupt nicht installiert ist
                // (z.B. fehlender Registry-Key/Geraetepfad) - in diesem Fall gilt die Funktion als nicht
                // verfuegbar, statt die gesamte Anwendung abstuerzen zu lassen.
                return false;
            }
        }
    }

    /// <summary>Ermittelt alle aktuell angeschlossenen PnP-Instanz-IDs, die zu einem physischen Geraet
    /// (identifiziert ueber Vendor-/Product-ID, siehe <see cref="PhysicalDeviceInfo"/>) gehoeren. Liefert eine
    /// leere Liste, falls das Geraet keine VID/PID besitzt (z.B. ein XInput-Geraet, zu dem sich kein
    /// eindeutiges DirectInput-Gegenstueck finden liess, siehe <see cref="DeviceEnumerator"/>) oder aktuell
    /// keine passende Instanz gefunden wird.</summary>
    public IReadOnlyList<string> ResolveInstanceIds(PhysicalDeviceInfo device)
    {
        if (device.VendorId is not { } vendorId || device.ProductId is not { } productId)
        {
            return Array.Empty<string>();
        }

        string hardwareId = $"VID_{vendorId:X4}&PID_{productId:X4}";
        if (!Devcon.FindInDeviceClassByHardwareId(
                DeviceClassIds.HumanInterfaceDevices, hardwareId, out var instanceIds, presentOnly: true, allowPartial: true))
        {
            return Array.Empty<string>();
        }

        return instanceIds.ToList();
    }

    /// <summary>Erhoeht die Referenzzaehlung fuer jede der angegebenen PnP-Instanz-IDs und sperrt neu
    /// hinzugekommene Instanzen ueber HidHide. Aktiviert bei Bedarf den globalen HidHide-Schalter (siehe
    /// Klassen-Dokumentation). Ohne Wirkung, falls <paramref name="instanceIds"/> leer ist.</summary>
    public void Lock(IReadOnlyCollection<string> instanceIds)
    {
        if (instanceIds.Count == 0)
        {
            return;
        }

        bool hadAnyBefore = _refCounts.Count > 0;

        foreach (var instanceId in instanceIds)
        {
            if (_refCounts.TryGetValue(instanceId, out var count))
            {
                _refCounts[instanceId] = count + 1;
                continue;
            }

            _refCounts[instanceId] = 1;
            _service.AddBlockedInstanceId(instanceId);
        }

        if (!hadAnyBefore)
        {
            _service.IsActive = true;
        }

        PersistLockState();
    }

    /// <summary>Verringert die Referenzzaehlung fuer jede der angegebenen PnP-Instanz-IDs und entsperrt
    /// Instanzen, deren Zaehler dadurch auf 0 faellt. Deaktiviert den globalen HidHide-Schalter wieder, sobald
    /// keine eigenen Sperren mehr bestehen. Ohne Wirkung, falls <paramref name="instanceIds"/> leer ist.</summary>
    public void Unlock(IReadOnlyCollection<string> instanceIds)
    {
        if (instanceIds.Count == 0)
        {
            return;
        }

        foreach (var instanceId in instanceIds)
        {
            if (!_refCounts.TryGetValue(instanceId, out var count))
            {
                continue;
            }

            if (count <= 1)
            {
                _refCounts.Remove(instanceId);
                _service.RemoveBlockedInstanceId(instanceId);
            }
            else
            {
                _refCounts[instanceId] = count - 1;
            }
        }

        if (_refCounts.Count == 0)
        {
            _service.IsActive = false;
        }

        PersistLockState();
    }

    /// <summary>Schreibt die aktuell eigenen, gesperrten Instanz-IDs (<see cref="_refCounts"/>) in
    /// <see cref="LockStateFilePath"/>, damit <see cref="CleanupOrphanedLocks"/> beim naechsten App-Start
    /// erkennen kann, ob nach einem Absturz noch Sperren dieser App im Treiber uebrig sind. Fehler beim
    /// Schreiben werden verschluckt - diese reine Diagnose-Persistenz darf die Anwendung niemals zum
    /// Absturz bringen (analog zu <see cref="App.Diagnostics.DebugLog"/>, hier bewusst ohne Abhaengigkeit
    /// zum App-Projekt erneut implementiert, da <see cref="HidHideController"/> im Core-Projekt lebt).</summary>
    private void PersistLockState()
    {
        try
        {
            var directory = Path.GetDirectoryName(LockStateFilePath)!;
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(LockStateFilePath, JsonSerializer.Serialize(_refCounts.Keys.ToList()));
        }
        catch
        {
            // Reine Diagnose-Persistenz - darf die Anwendung niemals zum Absturz bringen.
        }
    }

    /// <summary>
    /// Erkennt und entfernt verwaiste HidHide-Sperren eines vorherigen, nicht ordnungsgemaess beendeten
    /// Programmlaufs (Absturz): ein regulaeres Beenden ruft <see cref="Unlock"/> fuer jede eigene Sperre auf
    /// und hinterlaesst dabei eine leere <see cref="LockStateFilePath"/>-Datei - enthaelt diese Datei beim
    /// Start dieser (frischen, noch leeren) Instanz trotzdem Eintraege, muss der vorherige Lauf abgestuerzt
    /// sein, ohne diese selbst gesetzten Sperren wieder aufzuheben. Muss genau einmal beim App-Start
    /// aufgerufen werden, bevor irgendein Controller gestartet wird (siehe <see cref="Engine.ControllerManager"/>).
    /// Deaktiviert den globalen HidHide-Schalter dabei nur, falls die Sperrliste des Treibers danach
    /// komplett leer ist - andernfalls koennten unabhaengig davon von einer anderen Anwendung gesetzte
    /// Sperren durch das Deaktivieren wirkungslos werden.
    /// </summary>
    /// <returns>Die tatsaechlich als verwaist erkannten und entfernten Instanz-IDs (leer, falls keine
    /// Bereinigung notwendig war), fuer eine optionale Hinweismeldung in der UI.</returns>
    public IReadOnlyList<string> CleanupOrphanedLocks()
    {
        List<string>? persistedInstanceIds;
        try
        {
            if (!File.Exists(LockStateFilePath))
            {
                return Array.Empty<string>();
            }

            persistedInstanceIds = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(LockStateFilePath));
        }
        catch
        {
            // Beschaedigte/nicht lesbare Datei -> keine verlaessliche Aussage ueber verwaiste Sperren
            // moeglich, daher sicherheitshalber nichts unternehmen statt eine Ausnahme zu werfen.
            return Array.Empty<string>();
        }

        if (persistedInstanceIds is null || persistedInstanceIds.Count == 0)
        {
            return Array.Empty<string>();
        }

        var removed = new List<string>();
        try
        {
            var currentlyBlocked = _service.BlockedInstanceIds.ToHashSet();
            foreach (var instanceId in persistedInstanceIds)
            {
                if (currentlyBlocked.Contains(instanceId))
                {
                    _service.RemoveBlockedInstanceId(instanceId);
                    removed.Add(instanceId);
                }
            }

            if (removed.Count > 0 && _service.BlockedInstanceIds.Count == 0)
            {
                _service.IsActive = false;
            }
        }
        catch
        {
            // Treiber nicht (mehr) verfuegbar - Bereinigung kann nicht durchgefuehrt werden.
            return Array.Empty<string>();
        }

        PersistLockState(); // _refCounts ist bei einer frischen Instanz leer -> Datei wird korrekt zurueckgesetzt.
        return removed;
    }
}

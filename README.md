# Virtual Controller

Windows-Tool zum Erstellen mehrerer virtueller Gamecontroller (Xbox 360 / DualShock 4 als
Windows-Backend, "Nintendo"-Layout ist rein kosmetisch), auf die beliebige angeschlossene
physische Controller frei gemappt werden koennen - inspiriert von x360ce, aber als
eigenstaendige, systemweit sichtbare Geraete (kein DLL-Hijacking pro Spiel).

## Funktionsumfang

- **Mehrere unabhaengige virtuelle Controller gleichzeitig**: jeder mit eigenem Namen,
  Layout, Polling-Rate und komplett eigener Mapping-Tabelle.
- **Freies Mapping**: beliebige Buttons/Achsen/D-Pad-Richtungen beliebiger physischer
  Quell-Controller (auch geraeteuebergreifend, z.B. zwei Controller teilen sich "Start")
  auf beliebige Buttons/Achsen/Trigger/D-Pad-Richtungen des virtuellen Ziel-Controllers.
  Achsen koennen invertiert oder in zwei unabhaengige Haelften aufgeteilt werden.
- **Zwei Eingabe-APIs**: XInput (Xbox-kompatible Controller, bis zu 4 Geraete, sehr geringe
  Latenz) und DirectInput (generische HID-Joysticks/Gamepads, PlayStation-Controller,
  aeltere Geraete) - beide gleichzeitig nutzbar.
- **Zwei Ziel-Backends ueber ViGEmBus**: Xbox 360 (XInput) und DualShock 4 (HID). Das
  Anzeige-Layout ("Xbox", "PlayStation", "Nintendo") ist unabhaengig davon rein kosmetisch,
  siehe [Bekannte Einschraenkung: Layouts](#bekannte-einschraenkung-layouts).
- **Modi je virtuellem Controller**: beliebig viele frei benannte Modi (z.B. "Flugmodus",
  "Rennen") mit jeweils eigener, unabhaengiger Mapping-Tabelle. Umschalten entweder ueber
  einen einzelnen Toggle-Ausloeser (schaltet zyklisch weiter) oder ueber einen eigenen
  Switch-Ausloeser je Modus (aktiviert diesen Modus direkt). Optional mit kurzer
  Bildschirmbenachrichtigung bei jedem Wechsel.
- **Praezise Achsen-Aufbereitung**: Kalibrierungs-Assistent fuer Wertebereich, Mittelpunkt
  und Deadzone (inkl. automatischer Stickdrift-Erkennung), sowie waehlbare Antwortkurve
  (Linear, Exponential, S-Kurve) je physischer Achse.
- **1000-Hz-Praezisions-Loop**: eigener High-Resolution-Timer je virtuellem Controller
  (Sub-Millisekunden-Genauigkeit unter Windows 10 1809+, sonst automatischer
  Spin-Wait-Fallback), rein im User-Mode, ohne zusaetzliche Admin-Rechte.
- **Automatische Hotplug-Erkennung**: neu angeschlossene oder getrennte physische Geraete
  werden per Hintergrund-Polling erkannt, ohne dass die App neu gestartet oder manuell
  aktualisiert werden muss (abschaltbar).
- **Geraete-/Eingabenverwaltung**: einzelne Geraete oder einzelne Eingaben umbenennen,
  deaktivieren oder komplett aus allen Auswahllisten ausblenden (z.B. fuer nicht relevante
  Geraete) - unabhaengig davon, welchem virtuellen Controller sie aktuell zugeordnet sind.
- **Optionale HidHide-Integration**: sperrt die einem virtuellen Controller zugeordneten
  physischen Geraete waehrend dessen Laufzeit fuer alle anderen Anwendungen, damit z.B. ein
  Spiel nicht gleichzeitig auf das physische UND das davon abgeleitete virtuelle Geraet
  reagiert. Erfordert den separat zu installierenden [HidHide](https://github.com/nefarius/HidHide)-Treiber;
  die zugehoerige Checkbox ist ausgegraut, solange dieser nicht installiert ist.
- **Tray-Icon**: laeuft bei Bedarf im Hintergrund weiter (Fenster schliessen minimiert ins
  Tray, ueber das Tray-Icon jederzeit wieder erreichbar).
- **Persistenz**: alle virtuellen Controller, Mapping-Tabellen und Geraete-Einstellungen
  werden automatisch als JSON unter `%AppData%\VirtualController\` gespeichert - aufgeteilt in
  je eine Datei pro virtuellem Controller (`Controllers\controller-{name}.json`), je eine Datei
  pro physischem Geraet (`Devices\device-{marke}-{name}.json`, z.B. `device-logitech-x56.json`)
  sowie eine kleine `settings.json` fuer allgemeine Einstellungen. Eine noch vorhandene alte,
  kombinierte `profiles.json` aus einer fruehen Version wird beim ersten Start automatisch in
  dieses Format aufgeteilt und zu `profiles.json.migrated` umbenannt.

## Voraussetzungen (einmalig, auf dem Zielrechner)

1. **Windows 10, Version 1809 oder neuer** (x64). Auf aelteren Versionen faellt die App fuer
   den Praezisions-Loop automatisch auf einen Spin-Wait-Fallback zurueck, ViGEmBus selbst
   unterstuetzt auch Windows 7/8.1.
2. **ViGEmBus-Treiber** (kernelseitiger virtueller USB-Bus, digital signiert, ~300 KB,
   kein Neustart erforderlich): https://github.com/nefarius/ViGEmBus/releases
   (im Repo als lokale Kopie unter `driver/ViGEmBus_1.22.0_x64_x86_arm64.exe` vorhanden).
   - Ohne diesen Treiber meldet die App beim Start "ViGEmBus nicht verfuegbar" und kann keine
     virtuellen Controller erzeugen (die App selbst braucht dafuer keine Admin-Rechte, nur die
     einmalige Treiber-Installation).
3. **Optional: HidHide-Treiber**, nur falls die Funktion "physische Geraete waehrend der
   Laufzeit sperren" genutzt werden soll: https://github.com/nefarius/HidHide/releases.
   Ohne diesen Treiber funktioniert die App vollstaendig normal, die entsprechende Checkbox
   ist dann lediglich ausgegraut.
4. **.NET 8 SDK/Runtime nur beim selbst Bauen aus dem Quellcode noetig** (siehe
   [Bauen](#bauen-aus-dem-quellcode)). Der fertige Release-Download (siehe unten) ist
   "self-contained" und bringt die .NET-Runtime bereits mit - es muss nichts zusaetzlich
   installiert werden.

## Installation (empfohlen: fertiger Download)

1. Aktuellstes Release herunterladen: [Releases-Seite](../../releases) - dort das ZIP-Archiv
   `VirtualController-win-x64.zip` laden (enthaelt die fertige `VirtualController.exe` sowie
   alle dafuer benoetigten nativen WPF-DLLs - **beide gehoeren zusammen in einen Ordner**,
   die EXE allein reicht nicht zum Starten aus).
2. ZIP an einen beliebigen Ort entpacken.
3. [Voraussetzungen](#voraussetzungen-einmalig-auf-dem-zielrechner) (mindestens ViGEmBus)
   installieren, falls noch nicht vorhanden.
4. `VirtualController.exe` starten.

## Bauen aus dem Quellcode

```
dotnet restore
dotnet build -c Release
```

## Ausfuehren (aus dem Quellcode)

```
dotnet run --project src/VirtualController.App -c Release
```

Oder die erzeugte `VirtualController.exe` unter `src/VirtualController.App/bin/Release/net8.0-windows/`
direkt starten.

## Release erstellen (fuer Maintainer)

Ein Push eines Git-Tags im Format `v<Major>.<Minor>.<Patch>` (z.B. `v1.0.0`) loest automatisch
den GitHub-Actions-Workflow `.github/workflows/release.yml` aus: dieser baut die self-contained
`win-x64`-EXE, packt sie zusammen mit den benoetigten nativen WPF-DLLs in ein ZIP und
veroeffentlicht beides als neues GitHub Release.

```
git tag v1.0.0
git push origin v1.0.0
```

Alternativ laesst sich der Workflow auch manuell ueber den "Actions"-Tab auf GitHub
("Run workflow") ausloesen, z.B. zum Testen ohne dafuer einen Tag anzulegen.

## Architektur-Kurzueberblick

- **VirtualController.Core** - reine Logik, kein UI:
  - `Virtual/` ViGEmBus-Wrapper (Xbox360/DualShock4), Layout-Definitionen
  - `Devices/` XInput- und DirectInput-Auslesen physischer Controller, Geraete-Erkennung,
    Eingabe-Erfassung, Kalibrierung, optionale HidHide-Integration
  - `Mapping/` Profil-Modell (virtueller Controller + Modi + Mapping-Tabelle) und die Mapping-Engine
  - `Timing/` High-Resolution-Loop (bis 1000 Hz+), rein User-Mode, kein Treiber noetig
  - `Engine/` Verbindet alles zu laufenden Sessions je virtuellem Controller
  - `Profiles/` JSON-Persistenz unter `%AppData%\VirtualController\` - aufgeteilt in
    `Controllers\controller-{name}.json` (je virtuellem Controller), `Devices\device-{marke}-{name}.json`
    (je physischem Geraet) und `settings.json` (allgemeine Einstellungen); migriert eine evtl. noch
    vorhandene alte, kombinierte `profiles.json` beim ersten Start automatisch in dieses Format
- **VirtualController.App** - WPF-Oberflaeche (MVVM via CommunityToolkit.Mvvm), Tray-Icon.

## Bekannte Einschraenkung: Layouts

Windows/ViGEmBus kennt technisch nur zwei virtuelle Zielgeraete: **Xbox 360** (XInput) und
**DualShock 4** (HID). Ein natives "Nintendo Switch Pro Controller"-Ziel existiert nicht. Das
"Nintendo"-Layout in der App ist daher rein kosmetisch (Button-Beschriftung) und nutzt intern
das Xbox360-Backend.


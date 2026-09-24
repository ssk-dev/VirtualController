# Virtual Controller

Windows-Tool zum Erstellen mehrerer virtueller Gamecontroller (Xbox 360 / DualShock 4 als
Windows-Backend, "Nintendo"-Layout ist rein kosmetisch), auf die beliebige angeschlossene
physische Controller frei gemappt werden koennen - inspiriert von x360ce, aber als
eigenstaendige, systemweit sichtbare Geraete (kein DLL-Hijacking pro Spiel).

## Voraussetzungen (einmalig, auf dem Zielrechner)

1. **.NET 8 SDK** (zum Bauen) bzw. mindestens die **.NET 8 Desktop Runtime** (zum reinen Ausfuehren).
   Download: https://dotnet.microsoft.com/download/dotnet/8.0
2. **ViGEmBus-Treiber** (kernelseitiger virtueller USB-Bus, digital signiert, ~300 KB,
   kein Neustart erforderlich): https://github.com/nefarius/ViGEmBus/releases
   - Ohne diesen Treiber meldet die App beim Start "ViGEmBus nicht verfuegbar" und kann keine
     virtuellen Controller erzeugen (die App selbst braucht dafuer keine Admin-Rechte, nur die
     einmalige Treiber-Installation).
3. Windows 10 Version 1809 oder neuer (fuer den High-Resolution-Timer, der den 1000-Hz-Loop ermoeglicht).
   Auf aelteren Versionen faellt die App automatisch auf einen Spin-Wait-Fallback zurueck.

## Bauen

```
dotnet restore
dotnet build -c Release
```

## Ausfuehren

```
dotnet run --project src/VirtualController.App -c Release
```

Oder die erzeugte `VirtualController.exe` unter `src/VirtualController.App/bin/Release/net8.0-windows/`
direkt starten.

## Architektur-Kurzueberblick

- **VirtualController.Core** - reine Logik, kein UI:
  - `Virtual/` ViGEmBus-Wrapper (Xbox360/DualShock4), Layout-Definitionen
  - `Devices/` XInput- und DirectInput-Auslesen physischer Controller, Geraete-Erkennung, Eingabe-Erfassung
  - `Mapping/` Profil-Modell (virtueller Controller + Mapping-Tabelle) und die Mapping-Engine
  - `Timing/` High-Resolution-Loop (bis 1000 Hz+), rein User-Mode, kein Treiber noetig
  - `Engine/` Verbindet alles zu laufenden Sessions je virtuellem Controller
  - `Profiles/` JSON-Persistenz unter `%AppData%\VirtualController\profiles.json`
- **VirtualController.App** - WPF-Oberflaeche (MVVM via CommunityToolkit.Mvvm), Tray-Icon.

## Bekannte Einschraenkung: Layouts

Windows/ViGEmBus kennt technisch nur zwei virtuelle Zielgeraete: **Xbox 360** (XInput) und
**DualShock 4** (HID). Ein natives "Nintendo Switch Pro Controller"-Ziel existiert nicht. Das
"Nintendo"-Layout in der App ist daher rein kosmetisch (Button-Beschriftung) und nutzt intern
das Xbox360-Backend.

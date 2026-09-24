namespace VirtualController.Core.Virtual;

/// <summary>
/// Liefert die Anzeigenamen fuer Elemente des virtuellen Controllers, wie sie tatsaechlich auf
/// dem jeweils gewaehlten <see cref="ControllerLayout"/> beschriftet sind (z.B. Xbox: A/B/X/Y,
/// PlayStation: Kreuz/Kreis/Quadrat/Dreieck, Nintendo: B/A/Y/X seitenverkehrt). Wird von der UI
/// genutzt, damit die "Ziel-Wert"-Auswahl in der Mapping-Tabelle nicht die internen, generischen
/// Enum-Namen (South/East/West/North, ...) zeigt, sondern die dem Nutzer vertrauten Beschriftungen
/// des tatsaechlich gewaehlten virtuellen Controllers.
/// </summary>
public static class VirtualControllerLabels
{
    public static string GetButtonLabel(ControllerLayout layout, VirtualButton button) => layout switch
    {
        ControllerLayout.PlayStation => button switch
        {
            VirtualButton.South => "Kreuz",
            VirtualButton.East => "Kreis",
            VirtualButton.West => "Quadrat",
            VirtualButton.North => "Dreieck",
            VirtualButton.LeftShoulder => "L1",
            VirtualButton.RightShoulder => "R1",
            VirtualButton.LeftThumbClick => "L3",
            VirtualButton.RightThumbClick => "R3",
            VirtualButton.Back => "Share",
            VirtualButton.Start => "Options",
            VirtualButton.Guide => "PS",
            VirtualButton.Share => "Touchpad",
            _ => button.ToString()
        },
        ControllerLayout.Nintendo => button switch
        {
            // Kosmetisch seitenverkehrt zu Xbox (siehe ControllerLayout-Dokumentation): A/B und X/Y vertauscht.
            VirtualButton.South => "B",
            VirtualButton.East => "A",
            VirtualButton.West => "Y",
            VirtualButton.North => "X",
            VirtualButton.LeftShoulder => "L",
            VirtualButton.RightShoulder => "R",
            VirtualButton.LeftThumbClick => "Linker Stick (Klick)",
            VirtualButton.RightThumbClick => "Rechter Stick (Klick)",
            VirtualButton.Back => "-",
            VirtualButton.Start => "+",
            VirtualButton.Guide => "Home",
            VirtualButton.Share => "Screenshot",
            _ => button.ToString()
        },
        _ => button switch // Xbox (Standard)
        {
            VirtualButton.South => "A",
            VirtualButton.East => "B",
            VirtualButton.West => "X",
            VirtualButton.North => "Y",
            VirtualButton.LeftShoulder => "LB",
            VirtualButton.RightShoulder => "RB",
            VirtualButton.LeftThumbClick => "Linker Stick (Klick)",
            VirtualButton.RightThumbClick => "Rechter Stick (Klick)",
            VirtualButton.Back => "Back",
            VirtualButton.Start => "Start",
            VirtualButton.Guide => "Guide",
            VirtualButton.Share => "Share",
            _ => button.ToString()
        }
    };

    public static string GetTriggerLabel(ControllerLayout layout, VirtualTrigger trigger) => layout switch
    {
        ControllerLayout.PlayStation => trigger switch
        {
            VirtualTrigger.LeftTrigger => "L2",
            VirtualTrigger.RightTrigger => "R2",
            _ => trigger.ToString()
        },
        ControllerLayout.Nintendo => trigger switch
        {
            VirtualTrigger.LeftTrigger => "ZL",
            VirtualTrigger.RightTrigger => "ZR",
            _ => trigger.ToString()
        },
        _ => trigger switch // Xbox (Standard)
        {
            VirtualTrigger.LeftTrigger => "LT",
            VirtualTrigger.RightTrigger => "RT",
            _ => trigger.ToString()
        }
    };

    /// <summary>Achsen-Beschriftungen sind layoutunabhaengig (alle drei Layouts nutzen dieselbe Stick-Anordnung).</summary>
    public static string GetAxisLabel(VirtualAxis axis) => axis switch
    {
        VirtualAxis.LeftStickX => "Linker Stick X",
        VirtualAxis.LeftStickY => "Linker Stick Y",
        VirtualAxis.RightStickX => "Rechter Stick X",
        VirtualAxis.RightStickY => "Rechter Stick Y",
        _ => axis.ToString()
    };

    /// <summary>D-Pad-Richtungen sind layoutunabhaengig (Kreuz-Layout ist auf allen drei Layouts identisch).</summary>
    public static string GetDPadLabel(DPadDirection direction) => direction switch
    {
        DPadDirection.None => "Keine",
        DPadDirection.Up => "Hoch",
        DPadDirection.UpRight => "Hoch-Rechts",
        DPadDirection.Right => "Rechts",
        DPadDirection.DownRight => "Runter-Rechts",
        DPadDirection.Down => "Runter",
        DPadDirection.DownLeft => "Runter-Links",
        DPadDirection.Left => "Links",
        DPadDirection.UpLeft => "Hoch-Links",
        _ => direction.ToString()
    };
}

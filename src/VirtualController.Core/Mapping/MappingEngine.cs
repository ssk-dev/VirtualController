using VirtualController.Core.Devices;
using VirtualController.Core.Virtual;

namespace VirtualController.Core.Mapping;

/// <summary>
/// Computes a virtual controller's output state from the current states of its assigned physical devices,
/// according to its mapping table. Called once per virtual controller on every polling loop tick. Stateless
/// and thread-safe as long as the supplied dictionaries are not modified concurrently.
/// </summary>
public static class MappingEngine
{
    /// <summary>
    /// Applies all profile mappings to the latest device states and writes the result to
    /// <paramref name="target"/>, which is reset first.
    /// </summary>
    /// <param name="profile">Virtual controller profile.</param>
    /// <param name="latestStates">Latest polled state for each physical device (key = DeviceId).</param>
    /// <param name="target">Reusable output state, updated in place.</param>
    /// <param name="deviceSettings">Optional device-wide settings (key = DeviceId), used to ignore physical
    /// inputs disabled in the configuration dialog independently of the mapping table. Null means no settings
    /// exist, so all inputs are enabled by default.</param>
    public static void Apply(
        VirtualControllerProfile profile,
        IReadOnlyDictionary<string, DeviceState> latestStates,
        VirtualPadState target,
        IReadOnlyDictionary<string, DeviceSettings>? deviceSettings = null)
    {
        target.Reset();

        bool dpadUp = false, dpadDown = false, dpadLeft = false, dpadRight = false;

        // Evaluate only the active mode's mapping table (see VirtualControllerProfile.ActiveMode). Without an
        // active mode (e.g. none has been created), target remains in the reset state set above.
        var activeMappings = profile.ActiveMode?.Mappings;
        if (activeMappings is null)
        {
            return;
        }

        foreach (var entry in activeMappings)
        {
            if (!latestStates.TryGetValue(entry.SourceDeviceId, out var state))
            {
                continue; // Source device is disconnected; ignore this entry.
            }

            if (!deviceSettings.IsInputEnabled(entry.SourceDeviceId, entry.SourceKind, entry.SourceIndex))
            {
                continue; // Physical input is disabled in the configuration dialog; ignore it.
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

        // Combine D-pad flags collected during the loop at the end so directions such as Up and Right can come
        // from separate mapping entries and still produce UpRight.
        if (target.DPad == DPadDirection.None)
        {
            target.DPad = DPadDirectionExtensions.FromFlags(dpadUp, dpadDown, dpadLeft, dpadRight);
        }
    }

    /// <summary>
    /// Checks whether a physical input referenced by <see cref="PhysicalInputTrigger"/> (e.g. a mode switch
    /// trigger) is active on the current tick: a button is pressed, an axis is beyond its deadzone, or a D-pad
    /// direction is active. Unlike <see cref="MappingEntry"/> evaluation above, triggers have no target or
    /// per-entry deadzone; axes use the device-wide calibration when available, otherwise the default (see
    /// <see cref="Devices.DeviceSettingsExtensions.ResolveDefaultAxisDeadzone"/>). Used by
    /// <see cref="Engine.ControllerSession"/> for toggle/switch trigger edge detection, regardless of active mode.
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
                // A digital button mapped to an axis (e.g. buttons used as a stick substitute) gives full deflection.
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
        // A physical axis has one calibration/deadzone/curve despite appearing in the catalog as two entries
        // (AxisPositive/AxisNegative), one per mapping direction. Always look up axis settings through the
        // canonical AxisPositive key for that axis index, independent of this mapping entry's direction.
        // Enabled state and custom names still use the key for the actual direction (see caller). Which slots
        // are physically unidirectional (0..1, trigger-like) depends on the source API: XInput uses slots 4/5
        // (RotationY/RotationZ; see XInputDeviceReader.FixedAvailableAxes), while those slots are bidirectional
        // rotation axes in DirectInput, where slots 6/7 (Slider0/Slider1) are unidirectional. Comparing indices
        // without accounting for the API would therefore be incorrect for one of them.
        bool isXInputSource = entry.SourceDeviceId.StartsWith("xinput:", StringComparison.Ordinal);
        bool isTriggerLikeSlot = isXInputSource
            ? entry.SourceIndex is (int)PhysicalAxisId.RotationY or (int)PhysicalAxisId.RotationZ
            : entry.SourceIndex is (int)PhysicalAxisId.Slider0 or (int)PhysicalAxisId.Slider1;
        var axisSettings = deviceSettings.TryGetInputSettings(entry.SourceDeviceId, PhysicalInputKind.AxisPositive, entry.SourceIndex);
        float raw = AxisSignalProcessor.Process(state.GetAxisRaw(entry.SourceIndex), axisSettings, bidirectional: !isTriggerLikeSlot);
        bool wantPositive = entry.SourceKind == PhysicalInputKind.AxisPositive;

        // For digital targets (button/D-pad/trigger), treat the axis as a threshold switch. The device-wide
        // deadzone in AxisSignalProcessor.Process has already set values inside its radius to zero, so > 0 suffices.
        float magnitude = wantPositive ? MathF.Max(raw, 0f) : MathF.Max(-raw, 0f);
        bool digitalPressed = magnitude > 0f;

        // Like magnitude, but preserves the sign instead of normalizing to 0..1. DirectionalOnly axis targets
        // need this value (see below); magnitude is always positive and would make an isolated axis half always
        // deflect the virtual axis positively, regardless of whether SourceKind is AxisPositive or AxisNegative.
        float directionalSignedValue = wantPositive ? MathF.Max(raw, 0f) : MathF.Min(raw, 0f);

        switch (entry.TargetKind)
        {
            case MappingTargetKind.Axis when entry.TargetAxis.HasValue:
                // DirectionalOnly uses only the physical axis half specified by SourceKind (isolated as the
                // sign-preserving directionalSignedValue) instead of passing through the full bidirectional
                // raw value. This allows two physical axis halves (e.g. Y+ and X+) to map to the same or different
                // virtual axes with independent inversion. The device-wide deadzone, including rescaling from
                // its boundary, is already applied to raw by AxisSignalProcessor.Process.
                float normalized = entry.DirectionalOnly ? directionalSignedValue : raw;
                if (entry.Invert) normalized = -normalized;
                SetAxis(target, entry.TargetAxis.Value, normalized);
                break;

            case MappingTargetKind.Trigger when entry.TargetTrigger.HasValue:
                // Trigger-like slots are already normalized to 0..1 by the reader (XInput triggers or
                // DirectInput sliders; see the API-specific isTriggerLikeSlot logic above) and have the
                // device-wide deadzone applied by AxisSignalProcessor.Process. Other axes (e.g. a stick mapped
                // as a trigger) use their magnitude instead.
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

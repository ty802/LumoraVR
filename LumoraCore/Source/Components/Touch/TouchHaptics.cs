// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Input;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components.Touch;

// Named strengths for touch feedback. Authors pick a feel, not an amplitude and a duration - the
// mapping to actual pulse parameters belongs to the runtime, not to every button in every world.
public enum TouchHaptics
{
    None,

    // Barely-there tick. Hover.
    Light,

    // Noticeable bump. Contact.
    Medium,

    // Heavy thump. Something committed.
    Strong,

    // Very short, sharp snap. A switch throwing over.
    Click,
}

// Fire-and-forget controller feedback for touch controls.
//
// Only ever pulses the LOCAL user's own hardware: haptics are a property of the machine holding the
// controller, so a remote user's contact events must not reach into this at all. Every entry point
// is a no-op when there is no VR hardware behind it, and says so once instead of per frame. -xlinka
public static class Haptics
{
    private static bool _loggedMissingDriver;

    // Silently does nothing unless user is the local user, VR is live, and the side resolves to a
    // controller.
    public static void Pulse(User? user, Chirality hand, TouchHaptics preset)
    {
        if (preset == TouchHaptics.None || user == null || hand == Chirality.None)
            return;

        var world = user.World;
        if (world == null || world.LocalUser != user)
            return;

        Pulse(hand, preset);
    }

    public static void Pulse(Chirality hand, TouchHaptics preset)
    {
        if (preset == TouchHaptics.None || hand == Chirality.None)
            return;

        var input = Engine.Current?.InputInterface;
        if (input == null || !input.IsVRActive)
            return;

        var controller = hand == Chirality.Left ? input.LeftController : input.RightController;
        if (controller == null)
        {
            if (!_loggedMissingDriver)
            {
                _loggedMissingDriver = true;
                LumoraLogger.Log("Haptics: VR is active but no controller device is registered on that side; touch feedback is silent.");
            }
            return;
        }

        var (amplitude, duration) = Describe(preset);

        // Frequency 0 hands the choice to the XR runtime. Naming one here gets silence on any
        // controller that cannot do it, and controllers disagree about what is usable. -xlinka
        controller.TriggerHaptic(amplitude, duration, 0f);
    }

    // Amplitude (0..1) and duration in seconds for a preset.
    public static (float amplitude, float duration) Describe(TouchHaptics preset)
        => preset switch
        {
            TouchHaptics.Light => (0.20f, 0.015f),
            TouchHaptics.Medium => (0.45f, 0.030f),
            TouchHaptics.Strong => (0.85f, 0.060f),
            TouchHaptics.Click => (0.70f, 0.008f),
            _ => (0f, 0f),
        };
}

// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Input;
using Lumora.Script;

namespace Lumora.Core.Scripting;

// Vibrates one of this machine's VR controllers, the right unless right is off, at strength 0 to 1 for
// seconds; 0.05 is a short tap. pulsed says whether a controller was there to take it: nothing happens
// without VR. Strength is clamped to 0 to 1 and seconds to 0 to 5, and a value that is not a number pulses
// nothing. A pulse asked for in the same frame as another replaces it.
[Node("Input/Haptic Pulse", Capability.Input)]
public sealed class HapticPulse : ActionNode
{
    public const float MaxSeconds = 5f;

    [return: Port("pulsed")]
    public static bool Run(ScriptContext ctx, bool right = true, float strength = 0.5f, float seconds = 0.05f)
        => Pulse(ScriptInput.Of(ctx), right ? Chirality.Right : Chirality.Left, strength, seconds);

    internal static bool Pulse(InputInterface? input, Chirality side, float strength, float seconds)
    {
        if (input == null || !input.IsVRActive || !float.IsFinite(strength) || !float.IsFinite(seconds))
            return false;
        var controller = side switch
        {
            Chirality.Left => input.LeftController,
            Chirality.Right => input.RightController,
            _ => null,
        };
        float amplitude = System.Math.Clamp(strength, 0f, 1f);
        float duration = System.Math.Clamp(seconds, 0f, MaxSeconds);
        if (controller == null || !controller.IsDeviceActive || amplitude <= 0f || duration <= 0f)
            return false;
        controller.TriggerHaptic(amplitude, duration, 0f);
        return true;
    }
}

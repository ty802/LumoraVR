// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Audio;
using Lumora.Core.Components.Audio;
using Lumora.Script;

namespace Lumora.Core.Scripting;

// Writes a reverb effect's settings from one value: every field, or none when any of them is refused.
[Node("Audio/Set Reverb Effect Settings", Capability.WorldWrite)]
public sealed class SetReverbEffectSettings : ActionNode
{
    public static void Run(ScriptContext ctx, ReverbEffect? effect, ReverbSettings settings)
    {
        if (ScriptReads.Visible(ctx, effect))
            ScriptReverb.Write(ctx, effect!.Fields, settings);
    }
}

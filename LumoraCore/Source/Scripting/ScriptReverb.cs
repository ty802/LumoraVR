// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Audio;
using Lumora.Core.Components.Audio;

namespace Lumora.Core.Scripting;

// A whole reverb written by a script: all eleven fields or none. Each field is put to ScriptWrites'
// refusals before any of them changes, so a refusal leaves the reverb exactly as it was rather than
// half rewritten. The numbers go in as given, and whatever holds them clamps to its own limits; a number
// that is not finite is a fault.
internal static class ScriptReverb
{
    public static void Write(ScriptContext ctx, ReverbFields fields, in ReverbSettings settings)
    {
        if (!settings.IsFinite)
            throw new ArgumentException("the reverb settings hold a number that is not finite");
        for (int i = 0; i < ReverbFields.Count; i++)
            ScriptWrites.Check(ctx, fields[i]);
        ScriptWrites.Write(ctx, fields[0], settings.PreDelay);
        ScriptWrites.Write(ctx, fields[1], settings.Crossover);
        ScriptWrites.Write(ctx, fields[2], settings.LowRT60);
        ScriptWrites.Write(ctx, fields[3], settings.MidRT60);
        ScriptWrites.Write(ctx, fields[4], settings.HFDamping);
        ScriptWrites.Write(ctx, fields[5], settings.EQ1Frequency);
        ScriptWrites.Write(ctx, fields[6], settings.EQ1Level);
        ScriptWrites.Write(ctx, fields[7], settings.EQ2Frequency);
        ScriptWrites.Write(ctx, fields[8], settings.EQ2Level);
        ScriptWrites.Write(ctx, fields[9], settings.Mix);
        ScriptWrites.Write(ctx, fields[10], settings.Level);
    }
}

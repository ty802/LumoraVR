// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Script;

namespace Lumora.Core.Scripting;

// 20 log10 of an amplitude's size: 1 is 0 dB, 0.5 about -6, 0 minus infinity.
[Node("Math/Audio/Amplitude To Decibels")]
public sealed class AmplitudeToDecibels : ValueNode
{
    [return: Port("decibels")]
    public static float Run(float amplitude) => ScriptAudioAnalysis.Decibels(amplitude);
}

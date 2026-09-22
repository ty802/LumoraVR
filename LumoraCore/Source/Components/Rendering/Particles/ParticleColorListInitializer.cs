// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

// Multiplies the emitted colour by one picked from a list. Optional per-entry weights bias the draw;
// a weight list whose length does not match the colour list is ignored outright rather than applied
// halfway, because a partly-weighted draw is a bug that looks like a design decision.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleColorListInitializer : ParticleModuleBase
{
    public readonly SyncFieldList<colorHDR> Colors = new();

    // Optional relative weights, one per colour. Leave empty for an even draw.
    public readonly SyncFieldList<float> Weights = new();

    private colorHDR[] _colors = global::System.Array.Empty<colorHDR>();
    private float[] _weights = global::System.Array.Empty<float>();

    internal override ParticleSimModule CreateSimModule() => new ColorListInitializer();

    internal override void PushParameters(ParticleSimModule module)
    {
        if (_colors.Length != Colors.Count)
            _colors = new colorHDR[Colors.Count];
        for (int i = 0; i < Colors.Count; i++)
            _colors[i] = Colors[i];

        if (_weights.Length != Weights.Count)
            _weights = new float[Weights.Count];
        for (int i = 0; i < Weights.Count; i++)
            _weights[i] = Weights[i];

        var list = (ColorListInitializer)module;
        list.Colors = _colors;
        list.Weights = _weights.Length == _colors.Length && _weights.Length > 0 ? _weights : null;
    }
}

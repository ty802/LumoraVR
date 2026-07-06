// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.Components;

/// <summary>
/// Generates points on this slot's local XZ plane, uniformly inside a circle (or on its edge when
/// Shell is set).
/// </summary>
[ComponentCategory("Transform/Point Generators")]
public class CirclePointGenerator : Component, IPointGenerator
{
    public readonly Sync<float> Radius;
    public readonly Sync<bool> Shell;

    public CirclePointGenerator()
    {
        Radius = new Sync<float>(this, 1f);
        Shell = new Sync<bool>(this, false);
    }

    public float3 GeneratePoint(Slot? space = null)
    {
        space ??= World.RootSlot;
        float angle = Random.Shared.NextSingle() * MathF.PI * 2f;
        // sqrt for uniform area density; shell pins to the rim.
        float r = Shell.Value ? Radius.Value : MathF.Sqrt(Random.Shared.NextSingle()) * Radius.Value;
        var local = new float3(MathF.Cos(angle) * r, 0f, MathF.Sin(angle) * r);
        return Slot.LocalPointToSpace(local, space);
    }
}

// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components.Utility;

// Builds a float2 out of two float fields, one per component: two sliders onto one position, a pair of values
// onto a size. A component with no source reads zero.
[ComponentCategory("Utility/Values")]
[DefaultUpdateOrder(-100)]
public class Float2Compose : Component
{
    public readonly SyncRef<IField<float>> X;

    public readonly SyncRef<IField<float>> Y;

    public readonly FieldDrive<float2> Target;

    public Float2Compose()
    {
        X = new SyncRef<IField<float>>(this);
        Y = new SyncRef<IField<float>>(this);
        Target = new FieldDrive<float2>(this) { LocalValueOnly = true };
    }

    public override void OnUpdate(float delta)
    {
        if (Target.IsLinkValid)
            Target.SetValue(new float2(X.Target?.Value ?? 0f, Y.Target?.Value ?? 0f));
    }
}

// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components.Utility;

// Builds a float3 out of three float fields, one per component. A component with no source reads zero.
[ComponentCategory("Utility/Values")]
[DefaultUpdateOrder(-100)]
public class Float3Compose : Component
{
    public readonly SyncRef<IField<float>> X;

    public readonly SyncRef<IField<float>> Y;

    public readonly SyncRef<IField<float>> Z;

    public readonly FieldDrive<float3> Target;

    public Float3Compose()
    {
        X = new SyncRef<IField<float>>(this);
        Y = new SyncRef<IField<float>>(this);
        Z = new SyncRef<IField<float>>(this);
        Target = new FieldDrive<float3>(this) { LocalValueOnly = true };
    }

    public override void OnUpdate(float delta)
    {
        if (Target.IsLinkValid)
            Target.SetValue(new float3(X.Target?.Value ?? 0f, Y.Target?.Value ?? 0f, Z.Target?.Value ?? 0f));
    }
}

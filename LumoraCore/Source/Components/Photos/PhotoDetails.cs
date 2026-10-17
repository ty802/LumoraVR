// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components;

// What a photo knows about itself: who took it, when, in which world and from where, through which camera
// and how wide, and who was in the world then. Written once, when the photo is made. -xlinka
[ComponentCategory("Assets/Textures")]
public class PhotoDetails : Component
{
    public readonly Sync<string> TakenBy = new();
    public readonly Sync<string> TakenById = new();
    // UTC, as ISO 8601.
    public readonly Sync<string> TakenAt = new();
    public readonly Sync<string> WorldName = new();
    public readonly Sync<string> Camera = new();
    public readonly Sync<float3> Position = new();
    public readonly Sync<floatQ> Rotation = new();
    public readonly Sync<float> FieldOfView = new();
    public readonly SyncFieldList<string> People = new();
}

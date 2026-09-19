// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components.Touch;

// Where a fingertip ray met a surface. Distance is along the ray the source cast, which starts behind
// the tip, so the source reads depth straight off it. Normal points at the side the surface is read
// from: a tip on that side is short of the face, a tip past it is pressing.
public readonly struct TouchSurfaceHit
{
    public readonly float Distance;
    public readonly float3 Point;
    public readonly float3 Normal;
    // Where on the surface, in the surface's own two coordinates (canvas units on a canvas, the unrolled
    // metres of the dash), handed back to the surface with the pointer update.
    public readonly float2 Local;

    public TouchSurfaceHit(float distance, in float3 point, in float3 normal, in float2 local)
    {
        Distance = distance;
        Point = point;
        Normal = normal;
        Local = local;
    }
}

// A flat thing a fingertip can press: a Helio canvas, or the dash surface standing in for one. The
// source finds these through the world's interaction target registry, so a surface is also an
// IInteractionTarget. Pointer ids are the source's (one per hand, in the 900 band) under
// UIInteractionSource.Touch, so a laser hovering the same surface never shares state with a poke.
public interface ITouchSurface
{
    bool AcceptsPhysicalTouch { get; }

    // The ray is the tip's own: it starts behind the tip and runs along the finger. False when it misses
    // the face, arrives from behind, or runs past rayLength.
    bool TryTouchProject(in float3 rayOrigin, in float3 rayDirection, float rayLength, out TouchSurfaceHit hit);

    void UpdateTouchPointer(int pointerId, in TouchSurfaceHit hit, bool pressed, User? actor);

    void ClearTouchPointer(int pointerId, User? actor);
}

// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Components.Scripting;
using Lumora.Core.Math;
using Lumora.Script;

namespace Lumora.Core.Scripting;

// What the drawing nodes hand the drawing layer of the world the script runs in, on the machine it runs on:
// the host for a Shared script, every machine for a Local one. Points are in space's own coordinates when a
// space is given and in the world otherwise; radii and text heights stay in metres. A space the script
// cannot see draws nothing, and neither does a shape with a point that is not a finite number. An unset
// colour (clear black) draws white, an unset rotation turns nothing, and seconds of 0 or less show a shape
// for one frame. A script that brings the layer past WarnShapes standing shapes or WarnLabels labels is
// warned. -xlinka
internal static class ScriptDrawings
{
    public const int WarnShapes = 20000;
    public const int WarnLabels = 1000;
    public const float LineRadius = 0.005f;

    [ThreadStatic]
    private static List<(float3 A, float3 B)>? _scratch;

    public static List<(float3 A, float3 B)> Scratch()
    {
        var list = _scratch ??= new List<(float3 A, float3 B)>();
        list.Clear();
        return list;
    }

    public static color Shade(color value)
        => value.r == 0f && value.g == 0f && value.b == 0f && value.a == 0f ? color.White : value;

    public static floatQ Turn(floatQ rotation)
    {
        float lengthSquared = rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z + rotation.w * rotation.w;
        if (!float.IsFinite(lengthSquared) || lengthSquared < 1e-12f)
            return floatQ.Identity;
        float scale = 1f / MathF.Sqrt(lengthSquared);
        return new floatQ(rotation.x * scale, rotation.y * scale, rotation.z * scale, rotation.w * scale);
    }

    public static float Radius(float radius) => radius > 0f && float.IsFinite(radius) ? radius : LineRadius;

    public static float Seconds(float seconds) => seconds > 0f && float.IsFinite(seconds) ? seconds : 0f;

    public static ScriptDrawingLayer? Layer(ScriptContext ctx, Slot? space)
        => space == null || ScriptReads.Reached(ctx, space, "space") ? ScriptDrawingLayer.For(ctx.World) : null;

    public static bool ToWorld(Slot? space, float3 point, out float3 world)
    {
        world = space == null ? point : space.LocalPointToGlobal(point);
        return Finite(point) && Finite(world);
    }

    public static void Line(ScriptContext ctx, float3 a, float3 b, color color, float radius, float seconds, Slot? space)
    {
        var lines = Scratch();
        lines.Add((a, b));
        Lines(ctx, lines, color, radius, seconds, space);
    }

    // Segments given in space's coordinates.
    public static void Lines(ScriptContext ctx, List<(float3 A, float3 B)> lines, color color, float radius, float seconds, Slot? space)
    {
        if (Layer(ctx, space) is not { } layer)
            return;
        int before = layer.ShapeCount;
        color = Shade(color);
        radius = Radius(radius);
        seconds = Seconds(seconds);
        foreach (var (a, b) in lines)
        {
            if (ToWorld(space, a, out var from) && ToWorld(space, b, out var to))
                layer.Add(ScriptDrawnShape.Segment(from, to, color, radius), seconds);
        }
        Counted(ctx, before, layer.ShapeCount);
    }

    // Triangles given in space's coordinates, three points each.
    public static void Faces(ScriptContext ctx, ReadOnlySpan<float3> points, color color, float seconds, Slot? space)
    {
        if (Layer(ctx, space) is not { } layer)
            return;
        int before = layer.ShapeCount;
        color = Shade(color);
        seconds = Seconds(seconds);
        for (int i = 0; i + 2 < points.Length; i += 3)
        {
            if (ToWorld(space, points[i], out var a) && ToWorld(space, points[i + 1], out var b) && ToWorld(space, points[i + 2], out var c))
                layer.Add(ScriptDrawnShape.Triangle(a, b, c, color), seconds);
        }
        Counted(ctx, before, layer.ShapeCount);
    }

    public static void Label(ScriptContext ctx, float3 position, string? text, float size, color color, float seconds, Slot? space)
    {
        if (string.IsNullOrEmpty(text) || Layer(ctx, space) is not { } layer || !ToWorld(space, position, out var at))
            return;
        int before = layer.LabelCount;
        layer.AddLabel(at, text, size > 0f && float.IsFinite(size) ? size : 0.1f, Shade(color), Seconds(seconds));
        if (before <= WarnLabels && layer.LabelCount > WarnLabels)
            ctx.Warn(ScriptWarning.Drawings, $"more than {WarnLabels:N0} drawn labels at once");
    }

    // An arrow along vector from its start: a shaft and a four-sided head a fifth of its length, at most
    // ten times the shaft's radius across. A vector of no length draws nothing.
    public static void Arrow(ScriptContext ctx, float3 start, float3 vector, color color, float radius, float seconds, Slot? space)
    {
        if (!ToWorld(space, start, out var from) || !ToWorld(space, start + vector, out var tip) || Layer(ctx, space) is not { } layer)
            return;
        var along = tip - from;
        float length = along.Length;
        if (!(length > 1e-6f))
            return;
        int before = layer.ShapeCount;
        color = Shade(color);
        radius = Radius(radius);
        seconds = Seconds(seconds);
        var direction = along / length;
        float headLength = length * 0.2f;
        float headRadius = MathF.Min(headLength * 0.5f, radius * 10f);
        var baseCenter = tip - direction * headLength;
        var across = float3.Cross(direction, MathF.Abs(direction.y) < 0.9f ? float3.Up : float3.Right).Normalized;
        var up = float3.Cross(across, direction).Normalized;
        layer.Add(ScriptDrawnShape.Segment(from, baseCenter, color, radius), seconds);
        Span<float3> corners = stackalloc float3[4];
        corners[0] = baseCenter + across * headRadius;
        corners[1] = baseCenter + up * headRadius;
        corners[2] = baseCenter - across * headRadius;
        corners[3] = baseCenter - up * headRadius;
        for (int i = 0; i < 4; i++)
            layer.Add(ScriptDrawnShape.Triangle(tip, corners[i], corners[(i + 1) % 4], color), seconds);
        layer.Add(ScriptDrawnShape.Triangle(corners[0], corners[1], corners[2], color), seconds);
        layer.Add(ScriptDrawnShape.Triangle(corners[0], corners[2], corners[3], color), seconds);
        Counted(ctx, before, layer.ShapeCount);
    }

    private static void Counted(ScriptContext ctx, int before, int after)
    {
        if (before <= WarnShapes && after > WarnShapes)
            ctx.Warn(ScriptWarning.Drawings, $"more than {WarnShapes:N0} drawn shapes at once");
    }

    private static bool Finite(float3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
}

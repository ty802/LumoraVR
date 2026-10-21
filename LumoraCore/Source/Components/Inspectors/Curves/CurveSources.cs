// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Reflection;
using Lumora.Core.Components.Assets;
using Lumora.Core.Components.Utility;
using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components;

// What the curve editor can open, and the one place that decides it.
//
// A clip: an AnimationProvider, member left empty.
// An imported avatar's animations: its ImportedAvatarAnimator, member empty or "clip:<name>".
// A curve field, recognised by shape rather than listed by hand, so a component added later is covered
// the day it uses one of these shapes:
//   - a float2 list named ...Curve: (x, y) points (a bone chain's falloff curves)
//   - a float list named ...Times beside a float list named ...Values: a paired curve (particle and
//     trail curves). Either half opens the pair, under the Times name.
//   - a list of GradientStop of a number, vector or color (gradients, curve drivers).
public static class CurveSources
{
    private const BindingFlags MemberFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static ICurveSource? Create(Component target, string? member, Component owner)
    {
        if (target == null || target.IsDestroyed)
            return null;
        if (target is ImportedAvatarAnimator imported)
            return new ImportedClipCurveSource(imported, member ?? "");
        if (string.IsNullOrEmpty(member))
            return target is AnimationProvider provider ? new ClipCurveSource(provider, owner) : null;
        return CreateField(target, member);
    }

    // The member name a list opens under, or null when the list is not a curve.
    public static string? CurveMemberFor(Component owner, ISyncMember list)
    {
        if (owner == null || list == null)
            return null;
        string? name = NameOf(owner, list);
        if (name == null)
            return null;
        if (list is SyncFieldList<float> && name.EndsWith("Values", StringComparison.Ordinal))
        {
            string times = name[..^"Values".Length] + "Times";
            return ValueOf(owner, times) is SyncFieldList<float> ? (CreateField(owner, times) != null ? times : null) : null;
        }
        return CreateField(owner, name) != null ? name : null;
    }

    private static ICurveSource? CreateField(Component owner, string member)
    {
        var value = ValueOf(owner, member);
        switch (value)
        {
            case SyncFieldList<float2> points when member.EndsWith("Curve", StringComparison.Ordinal):
                return new PointListCurveSource(owner, member, points);
            case SyncFieldList<float> times when member.EndsWith("Times", StringComparison.Ordinal):
            {
                string valuesName = member[..^"Times".Length] + "Values";
                return ValueOf(owner, valuesName) is SyncFieldList<float> values
                    ? new PairedListCurveSource(owner, member, times, values)
                    : null;
            }
        }

        // SyncList<GradientStop<T>> for a T the editor can split into channels.
        var type = value?.GetType();
        if (type == null || !type.IsGenericType || type.GetGenericTypeDefinition() != typeof(SyncList<>))
            return null;
        var element = type.GetGenericArguments()[0];
        if (!element.IsGenericType || element.GetGenericTypeDefinition() != typeof(GradientStop<>))
            return null;
        var valueType = element.GetGenericArguments()[0];
        if (!Drawable(valueType))
            return null;
        var sourceType = typeof(GradientStopCurveSource<>).MakeGenericType(valueType);
        return (ICurveSource?)Activator.CreateInstance(sourceType, owner, member, value);
    }

    private static bool Drawable(Type valueType)
    {
        var kind = (CurveValueKind)typeof(CurveChannels<>).MakeGenericType(valueType)
            .GetField(nameof(CurveChannels<float>.Kind), BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        return kind is CurveValueKind.Scalar or CurveValueKind.Whole;
    }

    private static object? ValueOf(Component owner, string member)
    {
        for (var type = owner.GetType(); type != null && type != typeof(Component); type = type.BaseType)
        {
            var field = type.GetField(member, MemberFlags | BindingFlags.DeclaredOnly);
            if (field != null)
                return field.GetValue(owner);
        }
        return null;
    }

    private static string? NameOf(Component owner, ISyncMember list)
    {
        for (var type = owner.GetType(); type != null && type != typeof(Component); type = type.BaseType)
        {
            foreach (var field in type.GetFields(MemberFlags | BindingFlags.DeclaredOnly))
            {
                if (ReferenceEquals(field.GetValue(owner), list))
                    return field.Name;
            }
        }
        return null;
    }
}

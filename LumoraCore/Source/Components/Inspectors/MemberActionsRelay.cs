// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Reflection;
using Helio.UI;
using Lumora.Core;
using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components;

// quick actions for one member row, opened from the compact "..." button next to the member chip:
// reset the value to its default, break an incoming drive/link, and vector helpers (normalize,
// spread one axis to all). opens as a page on the pressing user's radial context menu, so the same
// flow works on desktop and in VR. value writes go through the inspector undo history.
[ComponentCategory("Utility/Inspectors")]
public class MemberActionsRelay : Component
{
    public readonly SyncRef<IWorldElement> TargetMember;

    public MemberActionsRelay()
    {
        TargetMember = new SyncRef<IWorldElement>(this);
    }

    private IField? Field => TargetMember.Target as IField;

    private static readonly float[] ItemFill = { 0.16f, 0.15f, 0.24f, 0.92f };
    private static readonly float[] CancelFill = { 0.30f, 0.30f, 0.32f, 0.92f };

    [SyncMethod]
    public void OnActionsPressed(Button button, UIInteractionContext context)
    {
        var member = TargetMember.Target;
        if (member == null || member.IsDestroyed)
            return;

        var userRoot = context.Actor?.Root?.Slot;
        var menu = userRoot?.GetComponentInChildren<UI.ContextMenuSystem>();
        if (menu == null || menu.IsOpen.Value)
            return;

        var page = new UI.ContextMenuPage((member as ISyncMember)?.Name ?? "Member");
        var field = Field;

        if (field != null)
        {
            page.AddItem(new UI.ContextMenuItem
            {
                Label = "Reset Default",
                FillColor = ItemFill,
                OnPressed = _ => { menu.Close(); ResetToDefault(); },
            });
        }

        if (member is ILinkable linkable && linkable.IsLinked)
        {
            string label = linkable.IsDriven ? "Break Drive" : "Break Link";
            page.AddItem(new UI.ContextMenuItem
            {
                Label = label,
                FillColor = ItemFill,
                LabelColor = ToFill(InspectorUI.DangerColor),
                OnPressed = _ => { menu.Close(); BreakLink(); },
            });
        }

        // Vector helpers for float2/3/4-shaped values. floatQ is excluded on purpose: spreading one
        // quaternion component to all four produces garbage rotations (normalize alone would be fine,
        // but the rotation editor already works in euler angles, so the raw-component menu just confuses).
        var comps = field != null ? VectorFloatFields(field.ValueType) : null;
        if (comps != null)
        {
            page.AddItem(new UI.ContextMenuItem
            {
                Label = "Normalize",
                FillColor = ItemFill,
                OnPressed = _ => { menu.Close(); MutateVector(comps, Normalize); },
            });
            page.AddItem(new UI.ContextMenuItem
            {
                Label = "Set All Avg",
                FillColor = ItemFill,
                OnPressed = _ => { menu.Close(); MutateVector(comps, SetAllToAverage); },
            });
            for (int i = 0; i < comps.Length; i++)
            {
                int axis = i; // captured per item
                page.AddItem(new UI.ContextMenuItem
                {
                    Label = $"All = {comps[i].Name.ToUpperInvariant()}",
                    FillColor = ItemFill,
                    LabelColor = ToFill(AxisColor(i)),
                    OnPressed = _ => { menu.Close(); MutateVector(comps, v => SetAllToComponent(v, axis)); },
                });
            }
        }

        page.AddItem(new UI.ContextMenuItem
        {
            Label = "Cancel",
            FillColor = CancelFill,
            OnPressed = _ => menu.Close(),
        });

        menu.OpenPage(page, BuildMenuContext(this, context.Actor));
    }

    // menu context aimed at the pressing user's laser hand (right hand owns the menu on desktop) -
    // the edge-close raycast needs a pointer to track or the menu dismisses itself instantly.
    // shared by every inspector flow that opens a menu from a UI press.
    internal static UI.ContextMenuContext BuildMenuContext(Component source, User? actor)
    {
        Slot? pointer = null;
        var side = Input.Chirality.Right;
        var userRoot = actor?.Root?.Slot;
        if (userRoot != null)
        {
            foreach (var hand in userRoot.GetComponentsInChildren<Interaction.HandTool>())
            {
                if (hand.Laser == null)
                    continue;
                pointer = hand.Laser.Slot;
                side = hand.Side.Value;
                if (hand.Side.Value == Input.Chirality.Right)
                    break;
            }
        }
        return new UI.ContextMenuContext { Target = source.Slot, Pointer = pointer ?? source.Slot, Side = side };
    }

    // reset to the member's constructor-seeded default: read the same member off a fresh,
    // never-initialized twin instance of the owning worker (pure C# object - its sync members exist
    // after construction but nothing touches the world). declared attribute defaults win when
    // present; the CLR type default is the last resort. only members sitting DIRECTLY on a worker
    // resolve a twin default - a list element's MemberIndex is its list position, not a worker
    // member index. NEVER cache these into the shared WorkerInitInfo: the apply-defaults loop stamps
    // every non-null entry onto every NEW instance, and a captured first-instance value (a slot
    // already named by its creator) silently rewrites the whole world - that bug renamed every slot
    // 'Root' and broke spawn.
    private void ResetToDefault()
    {
        var field = Field;
        if (field == null || field.IsDestroyed)
            return;

        object? def = null;
        if (field is ISyncMember syncMember && field is SyncElement element && element.Parent is Worker worker)
        {
            var info = WorkerInitializer.GetInitInfo(worker.GetType());
            int index = syncMember.MemberIndex;
            if (info.DefaultValues != null && index >= 0 && index < info.DefaultValues.Length)
                def = info.DefaultValues[index];
            def ??= ConstructorDefault(worker.GetType(), index);
        }
        def ??= DefaultForType(field.ValueType);

        object? before = field.BoxedValue;
        field.BoxedValue = def!;
        InspectorUndo.RecordEdit(this, field, before, field.BoxedValue);
    }

    private static object? ConstructorDefault(Type workerType, int memberIndex)
    {
        try
        {
            if (Activator.CreateInstance(workerType) is not Worker twin)
                return null;
            var member = memberIndex >= 0 && memberIndex < twin.SyncMemberCount
                ? twin.GetSyncMember(memberIndex)
                : null;
            // Refs excluded: a twin's ref default is always null anyway, and its boxed form isn't a value.
            return member is IField field && member is not ISyncRef ? field.BoxedValue : null;
        }
        catch
        {
            return null; // a ctor that demands world context just falls through to the type default
        }
    }

    // releases the member's incoming drive/link so it becomes hand-editable again. a link IS a
    // reference member and releasing it just clears that reference, so the break records as an
    // ordinary field edit and undo re-points the link, which re-grants it.
    private void BreakLink()
    {
        var member = TargetMember.Target;
        if (member is not ILinkable linkable || member.IsDestroyed)
            return;
        var link = linkable.ActiveLink;
        if (link == null)
            return;

        if (link is not IField linkField)
        {
            link.ReleaseLink();
            return;
        }
        object? before = linkField.BoxedValue;
        link.ReleaseLink();
        InspectorUndo.RecordEdit(this, linkField, before, linkField.BoxedValue);
    }

    private static object? DefaultForType(Type type)
    {
        // Safety net when no captured default exists: a zero quaternion is INVALID (breaks all
        // downstream rotation math), so rotations always reset to identity.
        if (type == typeof(floatQ))
            return floatQ.Identity;
        if (type.IsValueType)
            return Activator.CreateInstance(type);
        return type == typeof(string) ? "" : null;
    }

    // public float fields named x/y/z/w in order - matches the vector value types. null when the
    // type isn't vector-shaped (fewer than two components) or is a rotation.
    private static FieldInfo[]? VectorFloatFields(Type type)
    {
        if (type == typeof(floatQ))
            return null;
        var found = new List<FieldInfo>(4);
        foreach (var name in new[] { "x", "y", "z", "w" })
        {
            var f = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (f == null || f.FieldType != typeof(float))
                break;
            found.Add(f);
        }
        return found.Count >= 2 ? found.ToArray() : null;
    }

    // reads the components, runs the transform, writes the whole value back (one undo step)
    private void MutateVector(FieldInfo[] comps, Func<float[], float[]?> transform)
    {
        var field = Field;
        if (field == null || field.IsDestroyed)
            return;

        object? working = field.BoxedValue;
        if (working == null)
            return;
        var values = new float[comps.Length];
        for (int i = 0; i < comps.Length; i++)
            values[i] = (float)comps[i].GetValue(working)!;

        var next = transform(values);
        if (next == null)
            return;

        object? before = field.BoxedValue; // separate boxing so the mutation below can't alias it
        for (int i = 0; i < comps.Length; i++)
            comps[i].SetValue(working, next[i]);
        field.BoxedValue = working;
        InspectorUndo.RecordEdit(this, field, before, field.BoxedValue);
    }

    private static float[]? Normalize(float[] v)
    {
        float sum = 0f;
        for (int i = 0; i < v.Length; i++)
            sum += v[i] * v[i];
        float mag = MathF.Sqrt(sum);
        if (mag < 1e-9f)
            return null; // zero vector has no direction to keep
        var result = new float[v.Length];
        for (int i = 0; i < v.Length; i++)
            result[i] = v[i] / mag;
        return result;
    }

    private static float[] SetAllToAverage(float[] v)
    {
        float sum = 0f;
        for (int i = 0; i < v.Length; i++)
            sum += v[i];
        float avg = sum / v.Length;
        var result = new float[v.Length];
        for (int i = 0; i < v.Length; i++)
            result[i] = avg;
        return result;
    }

    private static float[] SetAllToComponent(float[] v, int axis)
    {
        var result = new float[v.Length];
        for (int i = 0; i < v.Length; i++)
            result[i] = v[axis];
        return result;
    }

    private static color AxisColor(int axis) => axis switch
    {
        0 => InspectorUI.AxisXColor,
        1 => InspectorUI.AxisYColor,
        2 => InspectorUI.AxisZColor,
        _ => InspectorUI.TextColor,
    };

    private static float[] ToFill(color c) => new[] { c.r, c.g, c.b, c.a };
}

// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Lumora.Core;
using Lumora.Core.Math;

namespace Lumora.Core.Components;

// Live drive/link state for one member row: driven members read magenta, linked-but-not-driving
// cyan, a member that has gone away gray, and everything reverts to its base color the moment the
// link breaks. The built-time tint alone goes stale - drives attach and detach while the panel is
// open, and a drive that lands after the row was built would otherwise never show.
// Two targets, because a row signals state in two places: the LABEL (every row has one) and
// optionally the row's value WIDGET, which is how a reference row's readout button carries the same
// state color. One component polls for both so a row costs one update, and both read the SAME rule
// (InspectorUI.FieldStateColor) so they can never disagree.
// The widget target is the COLOR FIELD, not the graphic: Image and BorderedImage are siblings under
// Graphic and each declares its own Tint, so there is no shared component type to point at. Holding
// the field itself also means anything with a color - a border, a text - can be signalled the same
// way without touching this. -xlinka
[ComponentCategory("Utility/Inspectors")]
public class MemberStateTint : Component
{
    public readonly SyncRef<IWorldElement> TargetMember;
    public readonly SyncRef<Text> Label;
    public readonly Sync<color> BaseColor;

    // optional value-widget color field (a ref row's readout backing) on the same state
    public readonly SyncRef<IField<color>> StateTint;
    public readonly Sync<color> TintBaseColor;

    private int _appliedState = -1; // 0 normal, 1 linked, 2 driven, 3 broken

    public MemberStateTint()
    {
        TargetMember = new SyncRef<IWorldElement>(this);
        Label = new SyncRef<Text>(this);
        BaseColor = new Sync<color>(this, InspectorUI.MutedColor);
        StateTint = new SyncRef<IField<color>>(this);
        TintBaseColor = new Sync<color>(this, InspectorUI.RowColor);
    }

    public override void OnUpdate(float delta)
    {
        var label = Label.Target;
        var tint = StateTint.Target;
        bool hasLabel = label != null && !label.IsDestroyed;
        bool hasTint = tint != null && !tint.IsDestroyed;
        if (!hasLabel && !hasTint)
            return;

        // Broken means the MEMBER ITSELF is gone, not that it lacks link machinery: a member kind with
        // no links (a delegate, a bag) is perfectly normal and must keep its base color. -xlinka
        var member = TargetMember.Target;
        int state;
        if (member == null || member.IsDestroyed)
            state = 3;
        else if (member is ILinkable linkable)
            state = linkable.IsDriven ? 2 : (linkable.IsLinked ? 1 : 0);
        else
            state = 0;

        // Write only on transitions - a per-frame color write would re-mesh the row's chunk every frame.
        if (state == _appliedState)
            return;
        _appliedState = state;

        if (hasLabel)
            label!.Color.Value = StateColor(state, BaseColor.Value);
        // Through ApplyStateTint, never a raw write: a Helio button DRIVES its own backing tint from an
        // interaction ColorDriver, so a direct write here would survive exactly until the first hover.
        if (hasTint)
            InspectorUI.ApplyStateTint(tint, StateColor(state, TintBaseColor.Value));
    }

    private static color StateColor(int state, in color baseColor) => state switch
    {
        3 => InspectorUI.BrokenColor,
        2 => InspectorUI.DrivenColor,
        1 => InspectorUI.LinkedColor,
        _ => baseColor,
    };
}

// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Helio.UI;
using Lumora.Core;
using Lumora.Core.Components.Interaction;
using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components;

// Editor for SyncRef members: a compact square-button cluster then the target readout stretched
// across the remaining row. Cluster: pull the target's reference card, open the target in a new
// inspector window, then (after the wide target button) clear. Pressing the target button assigns
// a held ReferenceProxy card when the presser holds one, otherwise selects the target in this
// panel. Grip-releasing a card over the row assigns too (type-checked either way).
// The whole editor area is also a PULL source for the CURRENT TARGET: gripping the readout hands
// you a card for whatever the reference points at, so a target can be dragged from one field into
// another without going through the tree. That deliberately shadows the row-level source (which
// pulls the reference MEMBER itself, for wiring drives) - the row source still wins on the label half of
// the row, since the parent walk finds the nearest source first. -xlinka
[ComponentCategory("Utility/Inspectors")]
public class RefMemberEditor : MemberEditor, IProxyReceiver, IProxySource
{
    private readonly SyncRef<Text> _targetText;
    private readonly SyncRef<IField<color>> _targetTint;

    public RefMemberEditor()
    {
        _targetText = new SyncRef<Text>(this);
        _targetTint = new SyncRef<IField<color>>(this);
    }

    private ISyncRef? Ref => TargetMember.Target as ISyncRef;

    protected override void BuildUI(UIBuilder ui)
    {
        // Small square glyph buttons; text glyphs only, no icon art.
        ui.PushStyle();
        ui.MinWidth(26f);
        ui.PreferredWidth(26f);
        ui.FlexibleWidth(0f);
        ui.PushStyle();
        ui.TextColor(InspectorUI.AccentColor);
        ui.Button("≡", OnPullPressed);   // pull the target's card
        ui.Button("↑", OnOpenPressed);   // open in a new inspector window
        ui.PopStyle();
        ui.PopStyle();

        // Target readout is itself the assign/select button, stretched across the row.
        ui.PushStyle();
        ui.FlexibleWidth(1f);
        var targetButton = ui.Button("", OnTargetPressed);
        var text = targetButton.Slot.GetComponentInChildren<Text>();
        if (text != null)
        {
            InspectorUI.FillParent(text.RectTransform!);
            text.HorizontalAlignment.Value = TextHorizontalAlignment.Center;
            text.VerticalAlignment.Value = TextVerticalAlignment.Middle;
            // Target labels embed slot names, which are user content with inline style tags, and the
            // null/invalid states are rendered italic.
            text.RichText.Value = true;
            _targetText.Target = text;
        }
        // The readout's BACKING carries the field state color (driven magenta, linked cyan, broken
        // gray), so the row's state is readable from the widget you would actually press. Registered
        // with the row's live tint so it tracks drives attaching and detaching while the panel stays
        // open. -xlinka
        var backing = targetButton.Slot.GetComponent<Image>();
        _targetTint.Target = backing?.Tint!;
        BindStateTint(backing?.Tint, InspectorUI.RowColor);
        ui.PopStyle();

        ui.PushStyle();
        ui.MinWidth(26f);
        ui.PreferredWidth(26f);
        ui.FlexibleWidth(0f);
        ui.TextColor(InspectorUI.DangerColor);
        ui.Button("∅", OnClearPressed);  // null the reference
        ui.PopStyle();
    }

    [SyncMethod]
    public void OnPullPressed(Button button, UIInteractionContext context)
    {
        var target = Ref?.Target;
        if (target == null || target.IsDestroyed || World == null)
            return;
        var head = World.LocalUser?.Root?.HeadSlot;
        float3 point = context.WorldPoint;
        float3 offset = head != null && (head.GlobalPosition - point).LengthSquared > 1e-6f
            ? (head.GlobalPosition - point).Normalized * 0.25f
            : float3.Up * 0.1f;
        ReferenceProxy.Spawn(World, target, point + offset);
    }

    // grip on the readout hands over a card for the reference's CURRENT TARGET
    public IGrabbable? TryCreateProxy(Grabber grabber, in float3 spawnPoint)
    {
        var target = Ref?.Target;
        if (target == null || target.IsDestroyed || World == null)
            return null;
        return ReferenceProxy.Spawn(World, target, spawnPoint);
    }

    // opens a second inspector window beside this one; a component target comes pre-expanded
    [SyncMethod]
    public void OnOpenPressed(Button button, UIInteractionContext context)
    {
        var origin = Slot.GetComponentInParent<SceneInspectorPanel>();
        if (origin == null)
            return;
        var target = Ref?.Target;
        var slot = target as Slot ?? (target as Component)?.Slot;
        if (slot == null || slot.IsDestroyed)
            return;
        var panel = SceneInspectorPanel.SpawnAdjacent(origin, slot);
        if (panel != null && target is Component component)
            panel.ExpandedComponents.Add(component.ReferenceID.RawValue);
    }

    // a held card assigns; an empty hand selects the target here
    [SyncMethod]
    public void OnTargetPressed(Button button, UIInteractionContext context)
    {
        if (TryConsumeHeldProxy(context.Actor))
            return;

        var panel = Slot.GetComponentInParent<SceneInspectorPanel>();
        if (panel == null)
            return;
        var target = Ref?.Target;
        var jumpTo = target as Slot ?? (target as Component)?.Slot;
        if (jumpTo == null || jumpTo.IsDestroyed)
            return;
        panel.Selected.Target = jumpTo;
    }

    protected override void RefreshDisplay()
    {
        var text = _targetText.Target;
        if (text == null || text.IsDestroyed)
            return;

        text.Content.Value = Describe(Ref);
        text.Color.Value = Ref?.Target == null ? InspectorUI.MutedColor : InspectorUI.TextColor;

        // The state color can change without the VALUE changing (a drive attaching writes no value),
        // so the tint component owns the steady-state updates; this just keeps a fresh row honest.
        var backingTint = _targetTint.Target;
        if (backingTint != null && !backingTint.IsDestroyed)
            InspectorUI.ApplyStateTint(backingTint, FieldStateColor(InspectorUI.RowColor));
    }

    // The readout line: "&lt;name&gt; on &lt;component&gt; on &lt;slot&gt; (&lt;RefID&gt;)". A slot target
    // names itself, a component names its type and slot, a member names itself plus the component and
    // slot it lives on. The RefID is load-bearing, not decoration: without it two same-named targets
    // on two same-named slots read identically and there is no way to tell which one a field actually
    // points at. Empty and gone read as italic states rather than an empty button. -xlinka
    private static string Describe(ISyncRef? reference)
    {
        // The reference MEMBER itself is gone (component destroyed under an open panel), not merely empty.
        if (reference == null || (reference is IWorldElement element && element.IsDestroyed))
            return "<i>invalid</i>";

        var target = reference.Target;
        if (target == null)
            return "<i>null</i>";

        if (target is Slot targetSlot)
            return $"{targetSlot.SlotName.Value} ({target.ReferenceID})";

        var component = target as Component ?? FindOwningComponent(target);
        var slot = component?.Slot ?? FindOwningSlot(target);

        string where = component != null && !ReferenceEquals(component, target)
            ? $" on {component.GetType().Name} on {slot?.SlotName.Value}"
            : slot != null ? $" on {slot.SlotName.Value}" : "";

        string name = target is ISyncMember member && !string.IsNullOrEmpty(member.Name)
            ? member.Name!
            : target.GetType().Name;

        return $"{name}{where} ({target.ReferenceID})";
    }

    private static Component? FindOwningComponent(IWorldElement? element)
    {
        while (element != null)
        {
            if (element is Component component)
                return component;
            element = (element as SyncElement)?.Parent;
        }
        return null;
    }

    private static Slot? FindOwningSlot(IWorldElement? element)
    {
        while (element != null)
        {
            if (element is Slot slot)
                return slot;
            if (element is Component component)
                return component.Slot;
            element = (element as SyncElement)?.Parent;
        }
        return null;
    }

    [SyncMethod]
    public void OnClearPressed(Button button, UIInteractionContext context)
    {
        var reference = Ref;
        if (reference == null || IsReadOnly)
            return;
        object? before = Field?.BoxedValue;
        reference.Clear();
        if (Field is { } field)
            InspectorUndo.RecordEdit(this, field, before, field.BoxedValue);
        RefreshDisplay();
    }

    // grip released (or primary pressed) over this row while holding cards: assign the first compatible one
    public bool TryReceiveProxy(IReadOnlyList<IGrabbable> held, Grabber grabber)
    {
        var reference = Ref;
        // A driven reference is owned by its drive; dropping onto it would be silently reverted.
        if (reference == null || IsReadOnly)
            return false;

        for (int i = 0; i < held.Count; i++)
        {
            if (held[i] is not Component component || component.Slot == null)
                continue;
            var proxy = component.Slot.GetComponent<ReferenceProxy>();
            var element = proxy?.Target.Target;
            if (element == null)
                continue;

            object? before = Field?.BoxedValue;
            if (!TryAssign(reference, element))
                continue;

            if (Field is { } field && !Equals(before, field.BoxedValue))
                InspectorUndo.RecordEdit(this, field, before, field.BoxedValue);
            RefreshDisplay();
            proxy!.Consume(held[i], grabber);
            return true;
        }
        return false;
    }

    private bool TryConsumeHeldProxy(User? actor)
    {
        var root = actor?.Root?.Slot;
        if (root == null)
            return false;
        var grabbers = new List<Grabber>();
        CollectGrabbers(root, grabbers);
        foreach (var grabber in grabbers)
        {
            // Snapshot: a successful receive releases and destroys the card, mutating the hold list.
            if (!grabber.IsHoldingObjects)
                continue;
            var held = new List<IGrabbable>(grabber.GrabbedObjects);
            if (TryReceiveProxy(held, grabber))
                return true;
        }
        return false;
    }

    private static void CollectGrabbers(Slot slot, List<Grabber> result)
    {
        var grabber = slot.GetComponent<Grabber>();
        if (grabber != null)
            result.Add(grabber);
        foreach (var child in slot.Children)
            CollectGrabbers(child, result);
    }

    // The assignment ladder, widest match first: the element itself, then a reference field's current
    // target, then - for a SLOT card - the slot's own ActiveSelf field, then every component on it.
    // Order matters, since a slot card can satisfy several rungs and the most direct one has to win.
    // So ONE slot card can feed a component reference, a bool field, or a mesh field, without the
    // user having to know which sub-element the field actually wanted. -xlinka
    internal static bool TryAssign(ISyncRef reference, IWorldElement element)
    {
        if (TrySetGuarded(reference, element))
            return true;
        if (element is ISyncRef sourceRef && sourceRef.Target != null && TrySetGuarded(reference, sourceRef.Target))
            return true;
        if (element is Slot slot)
        {
            if (TrySetGuarded(reference, slot.ActiveSelf))
                return true;
            foreach (var component in slot.GetAllComponents())
            {
                if (TrySetGuarded(reference, component))
                    return true;
            }
        }
        return false;
    }

    // TrySet only type-checks; the assignment behind it still throws for a cross-world or local-only
    // target. A card carrying one of those must fail this rung and fall through to the next, not take
    // the whole grip-release path (and everything else queued behind it) down with it. -xlinka
    private static bool TrySetGuarded(ISyncRef reference, IWorldElement? element)
    {
        if (element == null || element.IsDestroyed)
            return false;
        try
        {
            return reference.TrySet(element);
        }
        catch (System.Exception ex)
        {
            Logging.Logger.Debug($"RefMemberEditor: cannot assign {element.GetType().Name} - {ex.Message}");
            return false;
        }
    }
}

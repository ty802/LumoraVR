
// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Helio.UI.Layout;
using Lumora.Core;
using Lumora.Core.Components.Interaction;
using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Helio.UI;

// root of a UI tree. attach to a slot to make it (and descendants) a UI subtree. - xlinka
public class Canvas : Component, ILaserPointerTarget
{
    private sealed class PointerState
    {
        public IUIInteractable? Hovered;
        public IUIInteractable? Pressed;
        public bool IsPressed;
    }

    private readonly Dictionary<int, PointerState> _pointers = new();
    private RectTransform? _rootRect;
    private GraphicChunkRoot? _chunkRoot;
    private GraphicsChunk? _rootChunk;

    public RectTransform? RootRectTransform => _rootRect;
    public GraphicChunkRoot? ChunkRoot => _chunkRoot;
    public GraphicsChunk? RootChunk => _rootChunk;
    public int InteractionTargetPriority => 1000;

    public InteractionDescription GetInteractionDescription(InteractionLaser laser)
    {
        return new InteractionDescription
        {
            Name = Slot?.SlotName.Value,
            Cursor = LaserCursor.Default,
        };
    }

    public bool TryGetLaserPointerHit(
        InteractionLaser laser,
        in float3 rayOrigin,
        in float3 rayDirection,
        float maxDistance,
        out LaserPointerHit hit)
    {
        hit = default;
        var source = GetInteractionSource(laser);
        int pointerId = GetPointerId(laser);
        if (!TryHitTest(rayOrigin, rayDirection, source, pointerId, out var uiHit))
        {
            return false;
        }

        if (uiHit.Context.Distance > maxDistance)
        {
            return false;
        }

        hit = new LaserPointerHit(uiHit.Context.Distance, uiHit.Context.WorldPoint);
        return true;
    }

    public void UpdateLaserPointer(
        InteractionLaser laser,
        int pointerId,
        in float3 rayOrigin,
        in float3 rayDirection,
        bool isPressed)
    {
        UpdatePointer(GetInteractionSource(laser), pointerId, rayOrigin, rayDirection, isPressed);
    }

    public void ClearLaserPointer(InteractionLaser laser, int pointerId)
    {
        ClearPointer(GetInteractionSource(laser), pointerId);
    }

    public UIHit? HitTest(float3 rayOrigin, float3 rayDirection)
    {
        return TryHitTest(rayOrigin, rayDirection, out var hit) ? hit : null;
    }

    public bool TryHitTest(float3 rayOrigin, float3 rayDirection, out UIHit hit)
    {
        return TryHitTest(rayOrigin, rayDirection, UIInteractionSource.Unknown, 0, out hit);
    }

    public bool TryHitTest(
        float3 rayOrigin,
        float3 rayDirection,
        UIInteractionSource source,
        int pointerId,
        out UIHit hit)
    {
        hit = default;
        if (!TryRayToCanvasPoint(rayOrigin, rayDirection, out var context, source, pointerId))
        {
            return false;
        }

        var candidate = default(HitCandidate);
        ScanHitSlot(Slot, in context, ref candidate);
        if (candidate.Interactable == null)
        {
            return false;
        }

        hit = new UIHit(candidate.Interactable, context);
        return true;
    }

    public void UpdatePointer(
        UIInteractionSource source,
        int pointerId,
        float3 rayOrigin,
        float3 rayDirection,
        bool isPressed)
    {
        if (!TryRayToCanvasPoint(rayOrigin, rayDirection, out var context, source, pointerId))
        {
            ClearPointer(source, pointerId);
            return;
        }

        var candidate = default(HitCandidate);
        ScanHitSlot(Slot, in context, ref candidate);
        var hovered = candidate.Interactable;
        int key = PointerKey(source, pointerId);

        if (!_pointers.TryGetValue(key, out var state))
        {
            state = new PointerState();
            _pointers[key] = state;
        }

        if (!ReferenceEquals(state.Hovered, hovered))
        {
            state.Hovered?.NotifyHoverExit(in context);
            state.Hovered = hovered;
            state.Hovered?.NotifyHoverEnter(in context);
        }

        if (isPressed && !state.IsPressed)
        {
            state.Pressed = hovered;
            state.Pressed?.NotifyPress(in context);
        }
        else if (isPressed && state.Pressed != null)
        {
            state.Pressed.NotifyDrag(in context);
        }
        else if (!isPressed && state.IsPressed)
        {
            var pressed = state.Pressed;
            pressed?.NotifyRelease(in context);

            if (pressed != null && ReferenceEquals(pressed, hovered))
            {
                pressed.NotifySubmit(in context);
            }

            state.Pressed = null;
        }

        state.IsPressed = isPressed;
    }

    public void ClearPointer(UIInteractionSource source, int pointerId)
    {
        int key = PointerKey(source, pointerId);
        if (!_pointers.TryGetValue(key, out var state))
        {
            return;
        }

        var context = new UIInteractionContext(this, source, pointerId, float2.Zero, float3.Zero, float3.Zero, float3.Zero, 0f);
        state.Hovered?.NotifyHoverExit(in context);
        state.Pressed?.NotifyRelease(in context);
        _pointers.Remove(key);
    }

    public override void OnStart()
    {
        base.OnStart();
        EnsureRoot();
    }

    public void EnsureRoot()
    {
        _rootRect ??= Slot.GetComponent<RectTransform>() ?? Slot.AttachComponent<RectTransform>();
        _chunkRoot ??= Slot.GetComponent<GraphicChunkRoot>() ?? Slot.AttachComponent<GraphicChunkRoot>();
        if (_rootChunk == null)
        {
            _rootChunk = new GraphicsChunk(this, _rootRect);
            _rootChunk.PrepareCompute();
            _rootChunk.SubmitChanges();
        }
    }

    public override void OnCommonUpdate()
    {
        base.OnCommonUpdate();
        RebuildGraphics();
    }

    public override void OnDestroy()
    {
        foreach (var pointer in _pointers)
        {
            var context = new UIInteractionContext(this, UIInteractionSource.Unknown, pointer.Key, float2.Zero, float3.Zero, float3.Zero, float3.Zero, 0f);
            pointer.Value.Hovered?.NotifyHoverExit(in context);
            pointer.Value.Pressed?.NotifyRelease(in context);
        }
        _pointers.Clear();
        base.OnDestroy();
    }

    private void RebuildGraphics()
    {
        EnsureRoot();
        if (_rootRect == null || _rootChunk == null)
        {
            return;
        }

        ComputeRects(Slot, null);
        _rootChunk.PrepareCompute();

        foreach (var graphic in Slot.GetComponentsInChildren<Graphic>(true))
        {
            if (!graphic.Enabled.Value)
            {
                continue;
            }

            graphic.PrepareCompute();
            if (graphic.RequiresPreGraphicsCompute)
            {
                var preGraphics = graphic.PreGraphicsCompute();
                if (!preGraphics.IsCompletedSuccessfully)
                {
                    preGraphics.AsTask().GetAwaiter().GetResult();
                }
            }

            _rootChunk.ContentRenderData.BeginGraphic();
            graphic.ComputeGraphic(_rootChunk.ContentRenderData);
        }

        _rootChunk.SubmitChanges();
    }

    // bottom-up: anchor-rect every descendant, then apply this layout, which propagates
    // overrides into descendant subtrees via ReflowAfterParentChanged. - xlinka
    private void ComputeRects(Slot slot, RectTransform? parent)
    {
        var rect = slot.GetComponent<RectTransform>();
        var nextParent = parent;

        if (rect != null)
        {
            rect.SetRegisteredCanvas(this);
            rect.SetRectParent(parent);
            rect.ClearRectChildren();

            if (parent != null)
            {
                parent.AddRectChild(rect);
            }

            rect.SetLocalComputeRect(ComputeRect(rect, parent));
            nextParent = rect;
        }

        foreach (var child in slot.Children)
        {
            ComputeRects(child, nextParent);
        }
        foreach (var child in slot.LocalChildren)
        {
            ComputeRects(child, nextParent);
        }

        if (rect != null)
        {
            ApplyLayout(slot, rect);
        }
    }

    // run this slot's layout (if any) and reflow each child's subtree against the new rects. - xlinka
    private void ApplyLayout(Slot slot, RectTransform rect)
    {
        var layout = slot.GetComponent<LayoutController>();
        if (layout == null || !layout.Enabled.Value) return;

        layout.PrepareCompute();
        layout.ArrangeChildren(rect.RectChildren);

        foreach (var child in slot.Children) ReflowAfterParentChanged(child);
        foreach (var child in slot.LocalChildren) ReflowAfterParentChanged(child);
    }

    // a child's rect was just overridden by its parent's layout. re-anchor descendants and re-run inner layout. - xlinka
    private void ReflowAfterParentChanged(Slot slot)
    {
        var rect = slot.GetComponent<RectTransform>();
        if (rect != null)
        {
            foreach (var c in slot.Children) ReanchorAndDescend(c, rect);
            foreach (var c in slot.LocalChildren) ReanchorAndDescend(c, rect);
            ApplyLayout(slot, rect);
        }
        else
        {
            foreach (var c in slot.Children) ReflowAfterParentChanged(c);
            foreach (var c in slot.LocalChildren) ReflowAfterParentChanged(c);
        }
    }

    private void ReanchorAndDescend(Slot slot, RectTransform parent)
    {
        var rect = slot.GetComponent<RectTransform>();
        if (rect != null)
        {
            rect.SetLocalComputeRect(ComputeRect(rect, parent));
            foreach (var c in slot.Children) ReanchorAndDescend(c, rect);
            foreach (var c in slot.LocalChildren) ReanchorAndDescend(c, rect);
            ApplyLayout(slot, rect);
        }
        else
        {
            foreach (var c in slot.Children) ReanchorAndDescend(c, parent);
            foreach (var c in slot.LocalChildren) ReanchorAndDescend(c, parent);
        }
    }

    private static Rect ComputeRect(RectTransform rect, RectTransform? parent)
    {
        if (parent == null)
        {
            return Rect.FromMinMax(rect.OffsetMin.Value, rect.OffsetMax.Value);
        }

        var parentRect = parent.LocalComputeRect;
        var parentMin = parentRect.Min;
        var parentSize = parentRect.Size;
        var min = parentMin + parentSize * rect.AnchorMin.Value + rect.OffsetMin.Value;
        var max = parentMin + parentSize * rect.AnchorMax.Value + rect.OffsetMax.Value;
        return Rect.FromMinMax(min, max);
    }

    private bool TryRayToCanvasPoint(
        float3 rayOrigin,
        float3 rayDirection,
        out UIInteractionContext context,
        UIInteractionSource source,
        int pointerId)
    {
        context = default;
        EnsureRoot();

        if (_rootRect == null || Slot == null || !Slot.IsActive)
        {
            return false;
        }

        float dirLength = rayDirection.Length;
        if (dirLength <= 0.000001f)
        {
            return false;
        }

        var normalizedDirection = rayDirection / dirLength;
        var localOrigin = Slot.GlobalPointToLocal(rayOrigin);
        var localDirection = Slot.GlobalDirectionToLocal(normalizedDirection);
        if (MathF.Abs(localDirection.z) <= 0.000001f)
        {
            return false;
        }

        float t = -localOrigin.z / localDirection.z;
        if (t < 0f)
        {
            return false;
        }

        var localPoint3 = localOrigin + localDirection * t;
        var localPoint = new float2(localPoint3.x, localPoint3.y);
        var worldPoint = Slot.LocalPointToGlobal(localPoint3);
        float distance = (worldPoint - rayOrigin).Length;

        context = new UIInteractionContext(
            this,
            source,
            pointerId,
            localPoint,
            worldPoint,
            rayOrigin,
            normalizedDirection,
            distance);
        return true;
    }

    private void ScanHitSlot(Slot slot, in UIInteractionContext context, ref HitCandidate candidate)
    {
        foreach (var component in slot.Components)
        {
            switch (component)
            {
                case InteractionBlock block when block.BlocksPoint(context.LocalPoint):
                    candidate = HitCandidate.Blocked;
                    break;
                case IUIInteractable interactable when interactable.CanInteract && interactable.IsPointInside(context.LocalPoint):
                    candidate = new HitCandidate(interactable);
                    break;
            }
        }

        foreach (var child in slot.Children)
        {
            ScanHitSlot(child, in context, ref candidate);
        }
        foreach (var child in slot.LocalChildren)
        {
            ScanHitSlot(child, in context, ref candidate);
        }
    }

    private static int PointerKey(UIInteractionSource source, int pointerId)
    {
        return ((int)source << 24) ^ pointerId;
    }

    private static UIInteractionSource GetInteractionSource(InteractionLaser laser)
    {
        var input = Engine.Current?.InputInterface;
        if (input != null && !input.IsVRActive)
        {
            return UIInteractionSource.Desktop;
        }

        return laser.ControllerSide.Value == Chirality.Left
            ? UIInteractionSource.VRLeft
            : UIInteractionSource.VRRight;
    }

    private static int GetPointerId(InteractionLaser laser)
    {
        return laser.ControllerSide.Value == Chirality.Left ? 1 : 2;
    }

    private readonly struct HitCandidate
    {
        public static readonly HitCandidate Blocked = new HitCandidate(null);

        public readonly IUIInteractable? Interactable;

        public HitCandidate(IUIInteractable? interactable)
        {
            Interactable = interactable;
        }
    }
}

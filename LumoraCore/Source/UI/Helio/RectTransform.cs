// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Helio.UI.Layout;
using Lumora.Core;
using Lumora.Core.Math;

namespace Helio.UI;

// one per slot in a UI tree. anchored offsets relative to parent rect. - xlinka
// TODO - xlinka: layout algorithm not implemented yet, this is just the data carrier
[SingleInstancePerSlot]
public class RectTransform : Component
{
    [Flags]
    public enum DataModelFlag
    {
        None = 0,
        RectChanged = 1 << 0,
        LayoutChanged = 1 << 1,
        ComponentsChanged = 1 << 2,
        StructureChanged = 1 << 3,
        ChildrenOrderChanged = 1 << 4,
    }

    // anchors are in parent 0..1 space. offsets are in parent local units. - xlinka
    public readonly Sync<float2> AnchorMin;
    public readonly Sync<float2> AnchorMax;
    public readonly Sync<float2> OffsetMin;
    public readonly Sync<float2> OffsetMax;
    public readonly Sync<float2> Pivot;

    private Rect _localComputeRect;
    private DataModelFlag _dataModelFlags;
    private RectTransform? _rectParent;
    private readonly List<RectTransform> _rectChildren = new();
    private Canvas? _registeredCanvas;

    public RectTransform()
    {
        AnchorMin = new Sync<float2>(this, new float2(0.5f, 0.5f));
        AnchorMax = new Sync<float2>(this, new float2(0.5f, 0.5f));
        OffsetMin = new Sync<float2>(this, new float2(-50f, -50f));
        OffsetMax = new Sync<float2>(this, new float2(50f, 50f));
        Pivot = new Sync<float2>(this, new float2(0.5f, 0.5f));
    }

    public Rect LocalComputeRect => _localComputeRect;
    public Canvas? Canvas => _registeredCanvas;
    public RectTransform? RectParent => _rectParent;
    public IReadOnlyList<RectTransform> RectChildren => _rectChildren;
    public int ChildrenCount => _rectChildren.Count;

    public new void MarkChangeDirty()
    {
        _dataModelFlags |= DataModelFlag.RectChanged | DataModelFlag.LayoutChanged;
        SignalCanvasDirty();
    }

    public void MarkInvalidateHorizontalLayout()
    {
        _dataModelFlags |= DataModelFlag.LayoutChanged;
        SignalCanvasDirty();
    }

    public void MarkInvalidateVerticalLayout()
    {
        _dataModelFlags |= DataModelFlag.LayoutChanged;
        SignalCanvasDirty();
    }

    // called by UIComputeComponents on this slot when enable/disable/attach/destroy happens - xlinka
    public void NotifyComponentsChanged()
    {
        _dataModelFlags |= DataModelFlag.ComponentsChanged;
        SignalCanvasDirty();
    }

    public override void OnChanges()
    {
        base.OnChanges();
        MarkChangeDirty();
    }

    private void SignalCanvasDirty()
    {
        if (_registeredCanvas != null)
        {
            _registeredCanvas.MarkDirty(this);
            return;
        }
        var s = Slot;
        while (s != null)
        {
            var canvas = s.GetComponent<Canvas>();
            if (canvas != null)
            {
                canvas.MarkDirty(this);
                return;
            }
            s = s.Parent;
        }
    }

    internal void SetLocalComputeRect(in Rect rect) => _localComputeRect = rect;
    internal void SetRegisteredCanvas(Canvas? canvas) => _registeredCanvas = canvas;
    internal void SetRectParent(RectTransform? parent) => _rectParent = parent;
    internal void AddRectChild(RectTransform child) => _rectChildren.Add(child);
    internal void RemoveRectChild(RectTransform child) => _rectChildren.Remove(child);
    internal void ClearRectChildren() => _rectChildren.Clear();
    internal DataModelFlag DataModelFlags => _dataModelFlags;
    internal void ClearDataModelFlags(DataModelFlag mask) => _dataModelFlags &= ~mask;
}

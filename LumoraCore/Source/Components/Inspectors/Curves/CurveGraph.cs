// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Helio.UI;
using Lumora.Core.Assets.Animation;
using Lumora.Core.Math;

namespace Lumora.Core.Components;

// The pointer side of the curve graph. Everything here runs on the machine of the user pressing: what
// the press lands on decides the gesture, the gesture edits the panel's keys or its synced view, and the
// release closes the edit into one undo step. Hover is kept local, it only changes what gets drawn.
[ComponentCategory("Utility/Inspectors")]
public sealed class CurveGraph : InteractionElement, IUIAxisActionReceiver
{
    public readonly SyncRef<CurveStudioPanel> Panel;

    private const float HandleLength = 64f;
    private const float HandlePickRadius = 12f;
    private const double DoublePressSeconds = 0.4;
    private const float DoublePressDistance = 24f;
    private const float WheelZoom = 0.85f;

    private enum Gesture
    {
        None,
        Key,
        InHandle,
        OutHandle,
        Pan,
        Scrub,
    }

    private Gesture _gesture;
    private float2 _pressPoint;
    private float2 _pressTimeView;
    private float2 _pressValueView;
    private float2 _grabOffset;
    private int _dragKey = -1;
    private int _dragChannel;
    private List<CurveKeyRow>? _dragRows;

    private DateTime _lastPress = DateTime.MinValue;
    private float2 _lastPressPoint;

    public int HoverKey { get; private set; } = -1;

    public int HoverChannel { get; private set; } = -1;

    public CurveGraph()
    {
        Panel = new SyncRef<CurveStudioPanel>(this);
    }

    // Every direction of a drag means something here, so it never goes to a scrolling ancestor.
    public override bool PassDragToParent(in float2 dragDelta) => false;

    public bool TryView(out CurveView view)
    {
        view = default;
        var panel = Panel.Target;
        var rect = RectTransform?.LocalComputeRect;
        if (panel == null || !rect.HasValue || rect.Value.IsEmpty)
            return false;
        view = new CurveView(rect.Value, panel.TimeView.Value, panel.ValueView.Value);
        return view.IsValid;
    }

    // Where a key's tangent handles sit on screen: a fixed length along the slope, so a steep tangent
    // stays as easy to grab as a flat one.
    public static void HandlePoints(in CurveView view, CurveKeyRow row, int channel, out float2 inPoint, out float2 outPoint)
    {
        var key = view.Point(row.Time, row.Values[channel]);
        inPoint = key - Direction(view, row.In[channel]) * HandleLength;
        outPoint = key + Direction(view, row.Out[channel]) * HandleLength;
    }

    private static float2 Direction(in CurveView view, float slope)
    {
        var d = new float2(view.PixelsPerSecond, slope * view.PixelsPerUnit);
        float length = d.Length;
        return length > 1e-6f ? d / length : new float2(1f, 0f);
    }

    // PRESS

    protected override void OnPress(in UIInteractionContext context)
    {
        var panel = Panel.Target;
        if (panel == null || !TryView(out var view))
            return;
        var point = context.PointIn(Slot);
        _pressPoint = point;
        _pressTimeView = panel.TimeView.Value;
        _pressValueView = panel.ValueView.Value;
        _gesture = Gesture.None;

        if (view.InRuler(point))
        {
            _gesture = Gesture.Scrub;
            panel.ScrubTo(panel.SnapTime(view.Time(point.x)));
            return;
        }

        var info = panel.Info;
        var rows = panel.Rows;
        bool editable = panel.CanEditKeys;

        // The selected key's tangent handles come first: they sit on top of everything else.
        int selected = panel.Key.Value;
        int active = panel.Channel.Value;
        if (editable && info != null && info.AllowSmooth && selected >= 0 && selected < rows.Count
            && active >= 0 && active < info.Channels.Length && rows[selected].Shape == KeyframeInterpolation.Cubic)
        {
            HandlePoints(view, rows[selected], active, out var inPoint, out var outPoint);
            if ((point - outPoint).Length <= HandlePickRadius)
            {
                StartKeyDrag(panel, selected, active, Gesture.OutHandle, view, point);
                return;
            }
            if ((point - inPoint).Length <= HandlePickRadius)
            {
                StartKeyDrag(panel, selected, active, Gesture.InHandle, view, point);
                return;
            }
        }

        if (PickKey(panel, view, point, out int key, out int channel))
        {
            if (panel.Channel.Value != channel)
                panel.Channel.Value = channel;
            panel.Key.Value = key;
            if (editable)
                StartKeyDrag(panel, key, channel, Gesture.Key, view, point);
            return;
        }

        // Empty space. A second press here drops a key on the active channel; otherwise it pans.
        var now = DateTime.UtcNow;
        bool second = (now - _lastPress).TotalSeconds <= DoublePressSeconds && (point - _lastPressPoint).Length <= DoublePressDistance;
        _lastPress = now;
        _lastPressPoint = point;
        if (second && editable && info != null && info.Channels.Length > 0)
        {
            _lastPress = DateTime.MinValue;
            int channelIndex = System.Math.Clamp(active, 0, info.Channels.Length - 1);
            var newRows = panel.CopyRows();
            int index = panel.InsertKey(newRows, view.Time(point.x), channelIndex, view.Value(point.y));
            panel.ApplyEdit(newRows, CurveStudioPanel.AddKeyStep);
            panel.Key.Value = index;
            return;
        }
        if (panel.Key.Value != -1)
            panel.Key.Value = -1;
        _gesture = Gesture.Pan;
    }

    private void StartKeyDrag(CurveStudioPanel panel, int key, int channel, Gesture gesture, in CurveView view, float2 point)
    {
        _gesture = gesture;
        _dragKey = key;
        _dragChannel = channel;
        panel.BeginGesture();
        _dragRows = panel.CopyRows();
        var row = _dragRows[key];
        _grabOffset = new float2(row.Time - view.Time(point.x), row.Values[channel] - view.Value(point.y));
    }

    // The nearest key under the pointer, on the active channel first so overlapping channels never steal
    // a press from the one being worked on.
    private static bool PickKey(CurveStudioPanel panel, in CurveView view, float2 point, out int key, out int channel)
    {
        key = -1;
        channel = -1;
        var info = panel.Info;
        var rows = panel.Rows;
        if (info == null || rows.Count == 0)
            return false;
        float best = CurveStudioPanel.KeyPickRadius;
        int active = panel.Channel.Value;
        for (int pass = 0; pass < 2; pass++)
        {
            for (int c = 0; c < info.Channels.Length; c++)
            {
                if ((pass == 0) != (c == active) || !panel.ChannelVisible(c))
                    continue;
                for (int i = 0; i < rows.Count; i++)
                {
                    float d = (view.Point(rows[i].Time, rows[i].Values[c]) - point).Length;
                    if (d < best)
                    {
                        best = d;
                        key = i;
                        channel = c;
                    }
                }
            }
            if (key >= 0)
                return true;
        }
        return false;
    }

    // DRAG

    protected override void OnDrag(in UIInteractionContext context)
    {
        var panel = Panel.Target;
        if (panel == null || _gesture == Gesture.None)
            return;
        var point = context.PointIn(Slot);

        if (_gesture == Gesture.Pan)
        {
            var start = new CurveView(RectTransform!.LocalComputeRect, _pressTimeView, _pressValueView);
            if (!start.IsValid)
                return;
            float dt = (point.x - _pressPoint.x) / start.PixelsPerSecond;
            float dv = (point.y - _pressPoint.y) / start.PixelsPerUnit;
            panel.SetView(_pressTimeView - new float2(dt, dt), _pressValueView - new float2(dv, dv));
            return;
        }

        if (!TryView(out var view))
            return;

        if (_gesture == Gesture.Scrub)
        {
            panel.ScrubTo(panel.SnapTime(view.Time(point.x)));
            return;
        }

        var rows = _dragRows;
        var info = panel.Info;
        if (rows == null || info == null || _dragKey < 0 || _dragKey >= rows.Count || _dragChannel >= info.Channels.Length)
            return;
        var row = rows[_dragKey];

        if (_gesture == Gesture.Key)
        {
            float time = panel.SnapTime(view.Time(point.x) + _grabOffset.x);
            // Keys keep their order: a key dragged past its neighbour stops a hair short of it.
            float gap = System.Math.Max(1e-4f, (panel.Source?.SnapStep ?? 0.01f) * 0.5f);
            if (_dragKey > 0)
                time = MathF.Max(time, rows[_dragKey - 1].Time + gap);
            if (_dragKey < rows.Count - 1)
                time = MathF.Min(time, rows[_dragKey + 1].Time - gap);
            row.Time = panel.ClampTime(time);
            CurveStudioPanel.SetChannel(row, _dragChannel, panel.ShapeValue(view.Value(point.y) + _grabOffset.y));
            panel.UpdateGesture(rows);
            return;
        }

        // A tangent handle: the slope from the key to the pointer, the same on both sides.
        var key = view.Point(row.Time, row.Values[_dragChannel]);
        var d = point - key;
        if (_gesture == Gesture.InHandle)
            d = -d;
        if (d.x < 2f)
            d.x = 2f;
        float slope = (d.y / view.PixelsPerUnit) / (d.x / view.PixelsPerSecond);
        row.In[_dragChannel] = slope;
        row.Out[_dragChannel] = slope;
        panel.UpdateGesture(rows);
    }

    protected override void OnRelease(in UIInteractionContext context)
    {
        var panel = Panel.Target;
        var gesture = _gesture;
        _gesture = Gesture.None;
        _dragRows = null;
        if (panel == null)
            return;
        if (gesture == Gesture.Key)
            panel.EndGesture(CurveStudioPanel.MoveKeyStep);
        else if (gesture is Gesture.InHandle or Gesture.OutHandle)
            panel.EndGesture(CurveStudioPanel.TangentStep);
    }

    // HOVER

    protected override void OnHoverMove(in UIInteractionContext context)
    {
        var panel = Panel.Target;
        if (panel == null || !TryView(out var view))
            return;
        PickKey(panel, view, context.PointIn(Slot), out int key, out int channel);
        if (key == HoverKey && channel == HoverChannel)
            return;
        HoverKey = key;
        HoverChannel = channel;
        panel.Touch();
    }

    protected override void OnHoverExit(in UIInteractionContext context)
    {
        if (HoverKey < 0)
            return;
        HoverKey = -1;
        HoverChannel = -1;
        Panel.Target?.Touch();
    }

    // WHEEL / STICK

    // The wheel zooms time around the pointer, with shift the value axis. A stick's up-down zooms time
    // and its left-right the value axis.
    public bool ProcessAxis(in UIInteractionContext context, in float2 axis)
    {
        var panel = Panel.Target;
        if (panel == null || !TryView(out var view))
            return false;
        var keyboard = Engine.Current?.InputInterface?.Keyboard;
        bool shift = keyboard != null && (keyboard.IsKeyPressed(Input.Key.LeftShift) || keyboard.IsKeyPressed(Input.Key.RightShift));
        float timeNotches = shift ? 0f : axis.y;
        float valueNotches = shift ? axis.y : axis.x;
        if (MathF.Abs(timeNotches) < 1e-3f && MathF.Abs(valueNotches) < 1e-3f)
            return false;

        var point = context.PointIn(Slot);
        float t = view.Time(point.x);
        float v = view.Value(point.y);
        float tf = MathF.Pow(WheelZoom, timeNotches);
        float vf = MathF.Pow(WheelZoom, valueNotches);
        panel.SetView(
            new float2(t - (t - view.T0) * tf, t + (view.T1 - t) * tf),
            new float2(v - (v - view.V0) * vf, v + (view.V1 - v) * vf));
        return true;
    }

    public bool WantsAxis(in UIInteractionContext context) => true;
}

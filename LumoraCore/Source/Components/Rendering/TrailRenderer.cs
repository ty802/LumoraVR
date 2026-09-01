// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Assets;
using Lumora.Core.Math;
using Lumora.Simulation.Particles;

namespace Lumora.Core.Components;

// Streaks a ribbon behind whatever slot it sits on. The sword trail, the motion streak, the tyre mark.
//
// Same shape of data as a particle system's trail module and drawn by exactly the same geometry code -
// the difference is where the points come from. A particle trail has one strand per particle and the
// simulation lays the points; this has one strand, and the points are where this slot has BEEN.
//
// Points are recorded in a chosen SPACE, not in this slot's own frame, and that is the whole trick: a
// trail recorded in local space would ride along with the object and never streak at all. World space
// is the default and is right for a swung blade; a reference slot is for a trail that has to travel
// with something else, like a streak on a prop held inside a moving vehicle. -xlinka
//
// Recording happens in the late pass, after every driver and IK solver has finished posing the slot for
// the frame. A backgrounded world skips that pass entirely, which is correct: nobody is looking, and
// the trail resumes from where it left off rather than laying a straight line across the gap. -xlinka
[ComponentCategory("Rendering")]
public sealed class TrailRenderer : ImplementableComponent
{
    // Points past this are dropped oldest first. Changing it RESETS the trail.
    public const int MaxPointLimit = 512;

    [Group("Recording")]
    // False stops laying new points. What is already down still ages out, so the trail dies off
    // instead of vanishing.
    public readonly Sync<bool> Emit = new();

    // Space the points are recorded and drawn in. Empty means world space.
    public readonly SyncRef<Slot> Space = new();

    // Metres this slot has to travel before another point is committed. The head point is glued to the
    // slot every frame regardless, so the tip never detaches from the object.
    public readonly Sync<float> MinVertexDistance = new();

    // Seconds a point survives after being laid.
    public readonly Sync<float> MaxPointAge = new();

    public readonly Sync<int> MaxPoints = new();

    [Group("Shape")]
    // World diameter at the head, before the curve below.
    public readonly Sync<float> Width = new();

    // Key positions, 0 at the head (on the object) to 1 at the oldest end. An empty curve is a
    // constant width.
    public readonly SyncFieldList<float> WidthTimes = new();
    public readonly SyncFieldList<float> WidthValues = new();

    // Drawn vertices per control segment. 1 draws the raw polyline.
    public readonly Sync<int> Smoothing = new();

    [Group("Color")]
    public readonly Sync<colorHDR> Color = new();

    // Same 0-at-the-head convention as the width curve. An empty gradient leaves the tint alone.
    public readonly SyncFieldList<float> ColorTimes = new();
    public readonly SyncFieldList<colorHDR> ColorValues = new();

    public readonly Sync<float> EmissionStrength = new();

    [Group("Rendering")]
    public readonly Sync<StrandUVMode> UVMode = new();

    // World length of one texture repeat under TilePerSegment.
    public readonly Sync<float> TileLength = new();

    public readonly Sync<StrandAlignment> Alignment = new();

    // Face normal of the band under VelocityAligned, in the recording space.
    public readonly Sync<float3> AlignmentUp = new();

    public readonly AssetRef<TextureAsset> Texture = new();

    public readonly Sync<int> RenderQueue = new();

    // Metres past which the renderer stops drawing it. 0 never stops.
    public readonly Sync<float> MaxViewDistance = new();

    private static readonly float3[] EmptyPositions = System.Array.Empty<float3>();
    private static readonly colorHDR[] EmptyColors = System.Array.Empty<colorHDR>();
    private static readonly float[] EmptyWidths = System.Array.Empty<float>();

    // Committed points as a ring, newest at _head. Nothing is ever moved, so a trail at capacity does
    // not shuffle its whole history one slot along every time it lays a point.
    private float3[] _ringPositions = System.Array.Empty<float3>();
    private double[] _ringTimes = System.Array.Empty<double>();
    private int _head;
    private int _count;
    private int _capacity;

    private float3[] _positions = EmptyPositions;
    private colorHDR[] _colors = EmptyColors;
    private float[] _widths = EmptyWidths;
    private int _pointCount;

    private readonly FloatCurve _widthCurve = new();
    private readonly ColorGradient _colorGradient = new();
    private int _widthRevision = -1;
    private int _colorRevision = -1;

    private float3 _lastHead;
    private bool _haveHead;
    private Slot? _lastSpace;

    // Ordered head first, oldest last, in the recording space. Valid entries are [0, PointCount).
    public float3[] Positions => _positions;

    public colorHDR[] Colors => _colors;

    // Diameters, like the simulation publishes. The renderer offsets by half.
    public float[] Widths => _widths;

    public int PointCount => _pointCount;

    // Bumped when the published points actually changed. A parked, non-emitting trail stops bumping it
    // and the renderer stops rebuilding.
    public int Version { get; private set; }

    // Null means world space.
    public Slot? RecordingSpace => Space.Target;

    public override void OnInit()
    {
        base.OnInit();
        Emit.Value = true;
        MinVertexDistance.Value = 0.03f;
        MaxPointAge.Value = 0.5f;
        MaxPoints.Value = 48;
        Width.Value = 0.08f;
        Smoothing.Value = 4;
        Color.Value = colorHDR.White;
        EmissionStrength.Value = 1.4f;
        TileLength.Value = 1f;
        Alignment.Value = StrandAlignment.CameraFacing;
        AlignmentUp.Value = float3.Up;
        RenderQueue.Value = 60;
        MaxViewDistance.Value = 0f;
    }

    // Throws away the history. Anything that teleports the object has to call this or the trail draws a
    // streak across the map from where it used to be.
    public void ResetTrail()
    {
        _head = 0;
        _count = 0;
        _pointCount = 0;
        _haveHead = false;
        Version++;
    }

    public override void OnLateUpdate(float delta)
    {
        base.OnLateUpdate(delta);
        if (IsDestroyed || Slot == null)
            return;

        var space = Space.Target;
        if (!ReferenceEquals(space, _lastSpace))
        {
            _lastSpace = space;
            ResetTrail();
        }

        EnsureCapacity();
        RebuildCurves();

        double now = World?.Time.TotalTime ?? 0.0;
        var head = space != null && !space.IsDestroyed
            ? space.GlobalPointToLocal(Slot.GlobalPosition)
            : Slot.GlobalPosition;

        bool changed = Expire(now);
        changed |= Record(head, now);

        // A trail whose object has stopped and whose points have not expired is drawing the same
        // ribbon it drew last frame. Bumping the version anyway would rebuild it every frame for
        // nothing, which is the one cost this component is meant to be free of when idle.
        changed |= !_haveHead || (head - _lastHead).LengthSquared > 1e-10f;
        _lastHead = head;
        _haveHead = true;

        Publish(head);
        if (changed)
            Version++;
    }

    private bool Record(in float3 head, double now)
    {
        if (!Emit.Value || !Enabled || !Slot.IsActive)
            return false;

        if (_count == 0)
        {
            Push(head, now);
            return true;
        }

        float minimum = System.Math.Max(1e-4f, MinVertexDistance.Value);
        if ((head - _ringPositions[_head]).LengthSquared < minimum * minimum)
            return false;

        Push(head, now);
        return true;
    }

    private void Push(in float3 point, double time)
    {
        if (_capacity == 0)
            return;
        if (_count == 0)
        {
            _head = 0;
            _count = 1;
        }
        else
        {
            _head++;
            if (_head == _capacity)
                _head = 0;
            if (_count < _capacity)
                _count++;
        }
        _ringPositions[_head] = point;
        _ringTimes[_head] = time;
    }

    private bool Expire(double now)
    {
        float age = System.Math.Max(0f, MaxPointAge.Value);
        if (age <= 0f || _count == 0)
            return false;

        bool dropped = false;
        while (_count > 0)
        {
            int oldest = _head - (_count - 1);
            if (oldest < 0)
                oldest += _capacity;
            if (now - _ringTimes[oldest] <= age)
                break;
            _count--;
            dropped = true;
        }
        return dropped;
    }

    // Head first, so index 0 is the end sitting on the object and the curves read 0 there - the same
    // convention the simulation publishes strands in, which is what lets one geometry builder draw
    // both. -xlinka
    private void Publish(in float3 head)
    {
        int points = _count > 0 ? _count + 1 : 0;
        if (points < 2)
        {
            _pointCount = 0;
            return;
        }

        if (_positions.Length < points)
        {
            _positions = new float3[points];
            _colors = new colorHDR[points];
            _widths = new float[points];
        }

        float width = System.Math.Max(0f, Width.Value);
        var tint = Color.Value;
        float step = 1f / (points - 1);

        _positions[0] = head;
        for (int i = 1; i < points; i++)
        {
            int index = _head - (i - 1);
            if (index < 0)
                index += _capacity;
            _positions[i] = _ringPositions[index];
        }

        for (int i = 0; i < points; i++)
        {
            float t = i * step;
            _widths[i] = width * _widthCurve.Sample(t);
            _colors[i] = tint * _colorGradient.Sample(t);
        }

        _pointCount = points;
    }

    private void EnsureCapacity()
    {
        int wanted = System.Math.Clamp(MaxPoints.Value, 2, MaxPointLimit);
        if (wanted == _capacity)
            return;

        _capacity = wanted;
        _ringPositions = new float3[wanted];
        _ringTimes = new double[wanted];
        ResetTrail();
    }

    private void RebuildCurves()
    {
        ParticleCurveHelper.Rebuild(WidthTimes, WidthValues, _widthCurve, ref _widthRevision);
        ParticleCurveHelper.RebuildGradient(ColorTimes, ColorValues, _colorGradient, ref _colorRevision);
    }
}

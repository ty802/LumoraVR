// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components.Utility;

// Plays a sprite sheet by driving a material's UV scale and offset from a grid cell.
//
// Scale and offset are the two fields every material provider already has, so a flipbook needs no
// per-frame texture swap and no new asset plumbing: one texture, one material, the window moves.
//
// The frame is derived from the session clock when a rate is set, so every peer shows the same frame at
// the same moment. Frame is only read when the rate is zero - the component deliberately does not write
// its own frame number into a sync field every tick just to have it visible. -xlinka
[ComponentCategory("Utility/Materials")]
[DefaultUpdateOrder(-100)]
public class UVAtlasAnimator : Component
{
    public readonly Sync<int> Columns;

    public readonly Sync<int> Rows;

    // Zero uses the whole grid. Set it for a sheet whose last row is only part full.
    public readonly Sync<int> FrameCount;

    // Zero holds on Frame.
    public readonly Sync<float> FramesPerSecond;

    // The frame shown while the rate is zero. Wraps.
    public readonly Sync<int> Frame;

    // Sheets are almost always authored left to right from the TOP row, while V counts up from the
    // bottom. Clear this for a sheet laid out the other way.
    public readonly Sync<bool> TopDownRows;

    public readonly FieldDrive<float2> Scale;

    public readonly FieldDrive<float2> Offset;

    public UVAtlasAnimator()
    {
        Columns = new Sync<int>(this, 1);
        Rows = new Sync<int>(this, 1);
        FrameCount = new Sync<int>(this, 0);
        FramesPerSecond = new Sync<float>(this, 12f);
        Frame = new Sync<int>(this, 0);
        TopDownRows = new Sync<bool>(this, true);
        Scale = new FieldDrive<float2>(this) { LocalValueOnly = true };
        Offset = new FieldDrive<float2>(this) { LocalValueOnly = true };
    }

    public int Columns_Clamped => System.Math.Max(1, Columns.Value);

    public int Rows_Clamped => System.Math.Max(1, Rows.Value);

    public int Frames
    {
        get
        {
            int declared = FrameCount.Value;
            return declared > 0 ? declared : Columns_Clamped * Rows_Clamped;
        }
    }

    public int CurrentFrame
    {
        get
        {
            int frames = Frames;
            float rate = FramesPerSecond.Value;
            if (rate == 0f)
                return SelectionIndex.Wrap(Frame.Value, frames);

            // The clock is wall-clock anchored, so the raw frame count is in the tens of billions and
            // does not fit an int; wrap in long and only then narrow.
            long raw = (long)System.Math.Floor(UtilityClock.Seconds(World) * rate);
            return (int)(((raw % frames) + frames) % frames);
        }
    }

    public override void OnChanges()
    {
        base.OnChanges();
        // A held sheet still has to land on its cell when the grid is edited, and it never runs below.
        if (FramesPerSecond.Value == 0f)
            Apply(CurrentFrame);
    }

    public override void OnUpdate(float delta)
    {
        if (FramesPerSecond.Value == 0f)
            return;
        Apply(CurrentFrame);
    }

    private void Apply(int frame)
    {
        int columns = Columns_Clamped;
        int rows = Rows_Clamped;
        int column = frame % columns;
        int row = frame / columns;
        if (row >= rows)
            row %= rows;

        var scale = new float2(1f / columns, 1f / rows);
        float v = TopDownRows.Value ? (rows - row - 1) * scale.y : row * scale.y;
        Scale.SetValue(scale);
        Offset.SetValue(new float2(column * scale.x, v));
    }
}

// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

// Animates each particle through a grid of frames on one texture.
//
// The frame handed to the renderer is FRACTIONAL, not an integer index, so it can draw two frames and
// cross fade between them. A 12 fps sheet in front of a 90 fps headset strobes if the frame steps; the
// same sheet blended is continuous motion. The renderer decides whether to use the fraction, but the
// simulation always gives it the option. -xlinka
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleFlipbook : ParticleModuleBase
{
    [Group("Sheet")]
    public readonly Sync<int> Columns = new();
    public readonly Sync<int> Rows = new();

    // 0 uses the whole grid. Set it when the last row of the sheet is partly empty.
    public readonly Sync<int> FrameCount = new();

    [Group("Playback")]
    public readonly Sync<FlipbookDrive> Drive = new();

    public readonly Sync<FlipbookLoopMode> LoopMode = new();

    // Rate drive only.
    public readonly Sync<float> FramesPerSecond = new();

    // Lifetime drive only: times through the sheet across one particle's life.
    public readonly Sync<float> CycleCount = new();

    [Group("Start")]
    public readonly Sync<int> StartFrame = new();

    // Scatters the starting frame across the sheet so a burst does not animate in lockstep.
    public readonly Sync<bool> RandomStartFrame = new();

    public override void OnInit()
    {
        base.OnInit();
        Columns.Value = 4;
        Rows.Value = 4;
        FramesPerSecond.Value = 12f;
        CycleCount.Value = 1f;
        LoopMode.Value = FlipbookLoopMode.Loop;
        Drive.Value = FlipbookDrive.Lifetime;
    }

    internal override ParticleSimModule CreateSimModule() => new ParticleFlipbookModule();

    internal override void PushParameters(ParticleSimModule module)
    {
        var flipbook = (ParticleFlipbookModule)module;
        flipbook.Columns = Columns.Value;
        flipbook.Rows = Rows.Value;
        flipbook.FrameCount = FrameCount.Value;
        flipbook.Drive = Drive.Value;
        flipbook.LoopMode = LoopMode.Value;
        flipbook.FramesPerSecond = FramesPerSecond.Value;
        flipbook.CycleCount = CycleCount.Value;
        flipbook.StartFrame = StartFrame.Value;
        flipbook.RandomStartFrame = RandomStartFrame.Value;
    }
}

// Scatters the starting frame across a range, for sheets where only part of the grid is a sensible
// place to begin. Overrides the flipbook's own StartFrame for every particle it touches.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleFlipbookStartFrameInitializer : ParticleModuleBase
{
    public readonly Sync<int> MinFrame = new();
    public readonly Sync<int> MaxFrame = new();

    public override void OnInit()
    {
        base.OnInit();
        MaxFrame.Value = 1;
    }

    internal override ParticleSimModule CreateSimModule() => new FlipbookStartFrameInitializer();

    internal override void PushParameters(ParticleSimModule module)
    {
        var start = (FlipbookStartFrameInitializer)module;
        start.MinFrame = MinFrame.Value;
        start.MaxFrame = MaxFrame.Value;
    }
}

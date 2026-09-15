// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Media;

namespace Lumora.Core.Components.Media;

// Pushes a playhead into any float field: a slider's value, a progress bar's fill, a text formatter.
// Local-only writes, because the playback state it reads is itself replicated - every peer arrives at
// the same number from the same anchor, so broadcasting the result would double the traffic and fight
// the remote peer's own computation.
//
// WriteBack turns the same link into a scrubber. A drive normally owns its field outright, but a hand
// write is still allowed through (FieldDrive.IsModificationAllowed), so a value that moved away from
// what this wrote last frame can only have come from the user, and that is a seek. -xlinka
[ComponentCategory("Media")]
public sealed class PlaybackPositionDriver : Component
{
    public readonly SyncRef<Component> Source;

    // Drive 0..1 of the clip rather than seconds. A stream has no length, so the field holds -1.
    public readonly Sync<bool> UseNormalizedPosition;

    // A value set on the target by hand seeks the source instead of being overwritten.
    public readonly Sync<bool> WriteBack;

    public readonly FieldDrive<float> Target;

    private float _lastWritten;
    private bool _hasWritten;

    // Wide enough that a float round trip through the field never reads as a hand edit, tight enough
    // that a real scrub of a long clip still does.
    private const float WriteBackEpsilon = 1e-4f;

    public IPlayable? Playable => Source.Target as IPlayable;

    public PlaybackPositionDriver()
    {
        Source = new SyncRef<Component>(this);
        UseNormalizedPosition = new Sync<bool>(this, true);
        WriteBack = new Sync<bool>(this, false);
        Target = new FieldDrive<float>(this) { LocalValueOnly = true };
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);

        var playable = Playable;
        if (playable == null || !Target.IsLinkValid)
            return;

        bool normalized = UseNormalizedPosition.Value;

        // A -1 (no length known yet) that the slider clamped to 0 is not a hand edit.
        if (WriteBack.Value && _hasWritten && _lastWritten >= 0f)
        {
            float current = Target.DrivenValue;
            if (System.Math.Abs(current - _lastWritten) > WriteBackEpsilon)
            {
                if (normalized)
                    playable.NormalizedPosition = current;
                else
                    playable.Position = current;
            }
        }

        float value = normalized ? playable.NormalizedPosition : playable.Position;
        Target.SetValue(value);
        _lastWritten = value;
        _hasWritten = true;
    }
}

// The four transport states as bools, so a panel can swap a play icon for a pause icon, grey a stop
// button or show a "finished" plate without any of it being authored as state. Paused and Stopped are
// the two halves of "not playing": one keeps its position, the other is back at the start. -xlinka
[ComponentCategory("Media")]
public sealed class PlaybackStateDriver : Component
{
    public readonly SyncRef<Component> Source;

    public readonly FieldDrive<bool> IsPlaying;
    public readonly FieldDrive<bool> IsNotPlaying;
    public readonly FieldDrive<bool> IsPaused;
    public readonly FieldDrive<bool> IsStopped;

    public IPlayable? Playable => Source.Target as IPlayable;

    public PlaybackStateDriver()
    {
        Source = new SyncRef<Component>(this);
        IsPlaying = new FieldDrive<bool>(this) { LocalValueOnly = true };
        IsNotPlaying = new FieldDrive<bool>(this) { LocalValueOnly = true };
        IsPaused = new FieldDrive<bool>(this) { LocalValueOnly = true };
        IsStopped = new FieldDrive<bool>(this) { LocalValueOnly = true };
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);

        var playable = Playable;
        if (playable == null)
            return;

        bool playing = playable.IsPlaying;
        bool atStart = playable.Position == 0f;

        IsPlaying.SetValue(playing);
        IsNotPlaying.SetValue(!playing);
        IsPaused.SetValue(!playing && !atStart);
        IsStopped.SetValue(!playing && atStart);
    }
}

// Runs one playable off another: a second camera angle against the main video, an animation against a
// clip, a light flicker against a track. Rate and offset are there so the follower can run a fraction
// of the leader's timeline (a preview strip) or sit a fixed distance behind it.
//
// The position is pushed through the follower's own seek, which re-anchors and therefore replicates, so
// this is deliberately a LOCAL relationship: point it at a follower nobody else is driving. -xlinka
[ComponentCategory("Media")]
public sealed class PlaybackSynchronizer : Component
{
    public readonly SyncRef<Component> Source;
    public readonly SyncRef<Component> Target;

    public readonly Sync<bool> UseNormalizedPosition;
    public readonly Sync<float> PositionOffset;
    public readonly Sync<float> PositionRate;

    public IPlayable? SourcePlayable => Source.Target as IPlayable;
    public IPlayable? TargetPlayable => Target.Target as IPlayable;

    public PlaybackSynchronizer()
    {
        Source = new SyncRef<Component>(this);
        Target = new SyncRef<Component>(this);
        UseNormalizedPosition = new Sync<bool>(this, false);
        PositionOffset = new Sync<float>(this, 0f);
        PositionRate = new Sync<float>(this, 1f);
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);

        var source = SourcePlayable;
        var target = TargetPlayable;
        if (source == null || target == null || ReferenceEquals(source, target))
            return;

        if (source.IsPlaying)
        {
            if (!target.IsPlaying)
                target.Resume();
        }
        else if (target.IsPlaying)
        {
            target.Pause();
        }

        float rate = PositionRate.Value;
        bool normalized = UseNormalizedPosition.Value;
        float position = (normalized ? source.NormalizedPosition : source.Position) * rate + PositionOffset.Value;

        if (normalized)
            target.NormalizedPosition = position;
        else
            target.Position = position;

        target.Loop = source.Loop;
        target.Speed = source.Speed * rate;
    }
}

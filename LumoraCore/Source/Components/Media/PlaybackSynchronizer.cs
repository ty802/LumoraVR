// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Media;

namespace Lumora.Core.Components.Media;

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

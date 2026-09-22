// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Media;

namespace Lumora.Core.Components.Media;

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

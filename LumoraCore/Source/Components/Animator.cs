// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components;

/// <summary>
/// Plays animation clips on a slot hierarchy.
/// Drives slot transforms over time based on animation data.
/// </summary>
[ComponentCategory("Animation")]
public class Animator : Component
{
    /// <summary>
    /// Whether the animation loops.
    /// </summary>
    public Sync<bool> Loop { get; private set; }

    /// <summary>
    /// Playback speed multiplier.
    /// </summary>
    public Sync<float> Speed { get; private set; }

    /// <summary>
    /// Current playback position in seconds.
    /// </summary>
    public Sync<float> Position { get; private set; }

    /// <summary>
    /// Whether the animation is currently playing.
    /// </summary>
    public Sync<bool> Playing { get; private set; }

    public override void OnAwake()
    {
        base.OnAwake();
        Loop = new Sync<bool>(this, true);
        Speed = new Sync<float>(this, 1.0f);
        Position = new Sync<float>(this, 0.0f);
        Playing = new Sync<bool>(this, false);
    }
}

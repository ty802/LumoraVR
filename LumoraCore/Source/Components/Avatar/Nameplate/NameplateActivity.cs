// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar;

// What the status dot on a nameplate is allowed to say.
//
// Three states, because three are all we can prove. There is no Muted and no Disconnected here on
// purpose: nothing in the engine consumes User.IsMuted or User.IsSilenced (there is no voice path yet),
// and User.Ping / User.LastSyncMessage are declared but never written, so a mute dot or a connection dot
// would be a light with no wire behind it. Add them the day the signal is real. -xlinka
public enum NameplatePresence
{
    // Here and moving.
    Present,

    // Still here, but nothing has moved for a while.
    Idle,

    // The user's client has this world in the background - dashboard, another world, alt-tabbed. This is
    // the replicated User.IsPresent flag, not a guess.
    Away,
}

// Derives presence for ONE user, on the peer that is looking at them.
//
// Everything it reads is already on the wire: the body-node tracking streams and User.IsPresent. It adds
// no sync member and sends nothing - each peer reaches its own answer from the same replicated motion,
// which is also why two peers can briefly disagree by a sample interval and neither is wrong.
//
// Sampled at 4Hz rather than per frame. Idleness is a minute-scale question and a per-frame transform
// read per user per peer is a cost with no answer attached to it. -xlinka
public sealed class NameplateActivityWatcher
{
    private const float SampleInterval = 0.25f;

    // Metres. Below this the head has not moved, it has jittered - a seated desktop user's head is exactly
    // still, a tracked headset's never is, and the threshold is what keeps VR from reading as permanently
    // active while still letting a genuinely motionless tracker go idle.
    private const float MoveEpsilonSquared = 0.004f * 0.004f;

    // 1 - |dot| for two quaternions, about 1.6 degrees of turn.
    private const float TurnEpsilon = 1e-4f;

    private float _sampleCountdown;
    private float _stillSeconds;
    private bool _primed;

    private float3 _lastHead;
    private floatQ _lastHeadRotation;
    private float3 _lastLeft;
    private float3 _lastRight;

    public NameplatePresence State { get; private set; } = NameplatePresence.Present;

    public float IdleThreshold { get; set; } = 60f;

    public float StillSeconds => _stillSeconds;

    public void Reset()
    {
        _primed = false;
        _stillSeconds = 0f;
        _sampleCountdown = 0f;
        State = NameplatePresence.Present;
    }

    // Returns true when the state changed, so the caller only retints on a transition.
    public bool Tick(UserRoot? root, User? user, float delta)
    {
        var previous = State;

        if (user != null && !user.IsDestroyed && !user.IsPresent.Value)
        {
            // Backgrounded beats idle: they are not looking at this world at all, so how long their avatar
            // has been still says nothing useful.
            _stillSeconds = 0f;
            _primed = false;
            State = NameplatePresence.Away;
            return State != previous;
        }

        _sampleCountdown -= delta;
        if (_sampleCountdown > 0f)
            return false;
        _sampleCountdown = SampleInterval;

        var head = root?.HeadSlot;
        if (root == null || root.IsDestroyed || head == null || head.IsDestroyed)
        {
            // No body to read yet. Do not accrue idle time against a user whose avatar has not arrived.
            _primed = false;
            _stillSeconds = 0f;
            State = NameplatePresence.Present;
            return State != previous;
        }

        var headPosition = head.GlobalPosition;
        var headRotation = head.GlobalRotation;
        var left = root.LeftHandSlot?.GlobalPosition ?? headPosition;
        var right = root.RightHandSlot?.GlobalPosition ?? headPosition;

        bool moved = !_primed
            || (headPosition - _lastHead).LengthSquared > MoveEpsilonSquared
            || (left - _lastLeft).LengthSquared > MoveEpsilonSquared
            || (right - _lastRight).LengthSquared > MoveEpsilonSquared
            || Turned(_lastHeadRotation, headRotation);

        _lastHead = headPosition;
        _lastHeadRotation = headRotation;
        _lastLeft = left;
        _lastRight = right;
        _primed = true;

        if (moved)
            _stillSeconds = 0f;
        else
            _stillSeconds += SampleInterval;

        State = _stillSeconds >= IdleThreshold ? NameplatePresence.Idle : NameplatePresence.Present;
        return State != previous;
    }

    private static bool Turned(in floatQ a, in floatQ b)
    {
        float dot = floatQ.Dot(a, b);
        if (dot < 0f)
            dot = -dot;
        return 1f - dot > TurnEpsilon;
    }
}

public static class NameplatePresenceColors
{
    public static readonly color Present = new color(0.34f, 0.88f, 0.45f, 1f);
    public static readonly color Idle = new color(1f, 0.76f, 0.29f, 1f);
    public static readonly color Away = new color(0.52f, 0.55f, 0.62f, 1f);

    public static color For(NameplatePresence presence) => presence switch
    {
        NameplatePresence.Idle => Idle,
        NameplatePresence.Away => Away,
        _ => Present,
    };
}

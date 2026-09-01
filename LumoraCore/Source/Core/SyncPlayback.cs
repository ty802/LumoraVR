// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.IO;
using Lumora.Core.Components.Utility;
using Lumora.Core.Networking;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Persistence;

namespace Lumora.Core;

public enum PlaybackLoopMode : byte
{
    // Clamp to the range and stay on the last frame.
    Once = 0,

    Loop = 1,

    PingPong = 2
}

public static class PlaybackLoop
{
    public static float Wrap(float time, float length, PlaybackLoopMode mode)
    {
        if (!float.IsFinite(time))
            return 0f;

        // An unknown or empty length has nothing to wrap into. A media clip reports its length only
        // once it has been probed and a keyless animation never has one, so answering zero here would
        // park every reader at the start for as long as that lasts.
        if (length <= 0f)
            return time < 0f ? 0f : time;

        switch (mode)
        {
            case PlaybackLoopMode.Loop:
            {
                float t = time % length;
                return t < 0f ? t + length : t;
            }
            case PlaybackLoopMode.PingPong:
            {
                float period = length * 2f;
                float t = time % period;
                if (t < 0f)
                    t += period;
                return t <= length ? t : period - t;
            }
            default:
                return time < 0f ? 0f : (time > length ? length : time);
        }
    }

    // Loop and ping-pong never finish.
    public static bool IsFinished(float time, float length, PlaybackLoopMode mode)
        => mode == PlaybackLoopMode.Once && length > 0f && time >= length;
}

// Shared playback state - a playhead every peer computes instead of one somebody streams.
//
// The state is an ANCHOR (a session-clock instant plus the position the head was at then) and a rate
// (speed, playing, loop mode). Position at any instant is anchorPosition + (instant - anchor) * speed,
// wrapped by the loop mode. So a running clip costs sync traffic when someone presses play, pauses,
// scrubs or changes the speed, and NOTHING in between - and a peer joining an hour in computes the
// same frame from the same two numbers rather than sitting on frame zero until the next playhead
// packet lands. A streamed playhead would cost a float per player per frame and still be wrong for a
// joiner.
//
// The instant is SESSION-clock seconds, which is the whole reason this member can exist. World.Time
// starts at zero when each peer opens the world, so the same reading is a different real moment on
// every machine and an anchor written in it decodes to a different frame per peer. The session clock
// is the authority's reading with this peer's measured offset folded in, so the anchor travels raw
// with no per-receiver correction: whatever adjustment is owed was already paid once, centrally, by
// the clock. Playback agrees to within the offset estimate, which is tight enough for a body
// animation and is still NOT sample-accurate audio sync. -xlinka
//
// A KEYED curve member (sample an authored shape at Position) is deliberately not here: ValueGradient
// already does that over a SyncList of stops, and it says in its own header why LumoraMath's plain key
// arrays are the wrong storage for something the inspector has to edit and peers have to replicate
// stop by stop. Point a drive at ValueGradient.Progress from this member's NormalizedPosition instead
// of growing a second curve.
public class SyncPlayback : ConflictingSyncElement, ISyncMemberCopy
{
    private double _anchor;
    private float _anchorPosition;
    private float _speed = 1f;
    private bool _playing;
    private PlaybackLoopMode _loop;

    private float _length = -1f;

    public event Action<SyncPlayback>? OnPlaybackChange;

    // NOT state. The clip or the media behind it replicates on its own, so every peer's owner arrives
    // at the same number without this crossing the wire, and a peer whose asset has not loaded yet
    // gets the honest answer (unknown) rather than a length it cannot sample. Negative means unknown:
    // an unknown length never wraps and never finishes.
    public float Length
    {
        get => _length;
        set => _length = float.IsFinite(value) ? value : -1f;
    }

    public bool HasLength => _length > 0f;

    private double NowSeconds => UtilityClock.Seconds(World);

    // The instant the head was at AnchorPosition, in session-clock seconds.
    public double AnchorSeconds => _anchor;

    public float AnchorPosition => _anchorPosition;

    // The raw flag. IsPlaying is the one to ask - a Once clip that has run out still carries a set
    // flag, because clearing it would need a write from whichever peer noticed first.
    public bool Playing => _playing;

    public float Speed
    {
        get => _speed;
        set
        {
            value = float.IsFinite(value) ? value : 0f;
            if (value == _speed)
                return;

            // Re-anchor first or the head jumps: the elapsed time under the old anchor would be
            // replayed at the new rate. Anchoring at the position we are at right now keeps the frame
            // continuous through the speed change.
            if (_playing)
                InternalSetState(anchor: NowSeconds, anchorPosition: RawPositionAt(NowSeconds), speed: value);
            else
                InternalSetState(speed: value);
        }
    }

    public PlaybackLoopMode LoopMode
    {
        get => _loop;
        set
        {
            if (value == _loop)
                return;

            // Leaving a loop while past the end would otherwise clamp the head to the last frame with
            // no warning; re-anchoring at the wrapped position keeps it where the viewer saw it.
            if (_playing)
                InternalSetState(loop: value, anchor: NowSeconds, anchorPosition: PositionAt(NowSeconds));
            else
                InternalSetState(loop: value);
        }
    }

    // Wrapped into the range. Writing it re-anchors, so a scrub replicates as a state change rather
    // than a stream.
    public float Position
    {
        get => PositionAt(NowSeconds);
        set => Seek(value);
    }

    // Unwrapped. This is what tells a Once clip it has run out.
    public float RawPosition => RawPositionAt(NowSeconds);

    // -1 when the length is unknown, which is the only honest answer for a stream.
    public float NormalizedPosition
    {
        get => HasLength ? Position / _length : -1f;
        set
        {
            if (HasLength)
                Seek(value * _length);
        }
    }

    public bool IsPlaying => IsPlayingAt(NowSeconds);

    public bool IsFinished => !IsPlaying && _loop == PlaybackLoopMode.Once && HasLength
        && (_speed >= 0f ? RawPosition >= _length : RawPosition <= 0f);

    // Where the head starts for the current direction.
    public float StartPosition => _speed >= 0f ? 0f : (HasLength ? _length : 0f);

    // EVALUATION
    // All of it takes the instant explicitly so a caller can look ahead (the audio mixer wants the
    // position at the START of the buffer it is about to fill, not at whenever it got round to asking)
    // and so the maths is testable without a wall clock.

    public float RawPositionAt(double sessionSeconds)
    {
        if (!_playing || _speed == 0f)
            return _anchorPosition;
        return _anchorPosition + (float)((sessionSeconds - _anchor) * _speed);
    }

    public float PositionAt(double sessionSeconds)
        => PlaybackLoop.Wrap(RawPositionAt(sessionSeconds), _length, _loop);

    public bool IsPlayingAt(double sessionSeconds)
    {
        if (!_playing || IsDisposed)
            return false;
        if (_loop != PlaybackLoopMode.Once)
            return true;
        if (!HasLength)
            return true;

        float raw = RawPositionAt(sessionSeconds);
        if (!float.IsFinite(raw))
            return false;
        return _speed >= 0f ? raw < _length : raw > 0f;
    }

    // How many times a looping head has been round. Zero for anything that cannot loop.
    public int LoopCountAt(double sessionSeconds)
    {
        if (_loop == PlaybackLoopMode.Once || !HasLength)
            return 0;
        float raw = RawPositionAt(sessionSeconds);
        if (!float.IsFinite(raw))
            return 0;
        float period = _loop == PlaybackLoopMode.PingPong ? _length * 2f : _length;
        return (int)(raw / period);
    }

    // TRANSPORT

    // From the start, in the direction the speed points.
    public void Play() => InternalSetState(playing: true, anchor: NowSeconds, anchorPosition: StartPosition);

    // From wherever the head is. A finished Once clip starts over instead of sitting on the last frame.
    public void Resume()
    {
        if (IsPlaying)
            return;
        if (_loop == PlaybackLoopMode.Once && HasLength
            && (_speed >= 0f ? RawPosition >= _length : RawPosition <= 0f))
        {
            Play();
            return;
        }
        InternalSetState(playing: true, anchor: NowSeconds, anchorPosition: Position);
    }

    // Freezes the head where it is.
    public void Pause()
    {
        if (!_playing)
            return;
        InternalSetState(playing: false, anchor: NowSeconds, anchorPosition: Position);
    }

    // Stops AND rewinds.
    public void Stop() => InternalSetState(playing: false, anchor: NowSeconds, anchorPosition: StartPosition);

    public void Restart() => Play();

    public void Seek(float position)
    {
        if (!float.IsFinite(position))
            position = 0f;
        InternalSetState(anchor: NowSeconds, anchorPosition: position);
    }

    public void TogglePlayback()
    {
        if (IsPlaying)
            Pause();
        else
            Resume();
    }

    // Only re-seeks when the head has actually drifted past the tolerance, so a follower correcting
    // itself against an external source does not re-anchor (and so replicate) every single frame.
    public void SeekIfDrifted(float position, float tolerance)
    {
        float current = Position;
        float distance = _loop != PlaybackLoopMode.Once && HasLength
            ? WrappedDistance(current, PlaybackLoop.Wrap(position, _length, _loop), _length)
            : System.Math.Abs(current - position);
        if (!float.IsFinite(current) || distance > tolerance)
            Seek(position);
    }

    private static float WrappedDistance(float a, float b, float length)
    {
        float direct = System.Math.Abs(a - b);
        return System.Math.Min(direct, length - direct);
    }

    // STATE

    private void InternalSetState(
        bool? playing = null,
        PlaybackLoopMode? loop = null,
        double? anchor = null,
        float? anchorPosition = null,
        float? speed = null,
        bool sync = true,
        bool change = true)
    {
        if (!BeginModification(throwOnError: false))
            return;

        _playing = playing ?? _playing;
        _loop = loop ?? _loop;
        _anchor = anchor ?? _anchor;
        _anchorPosition = anchorPosition ?? _anchorPosition;
        _speed = speed ?? _speed;

        if (sync)
            InvalidateSyncElement();

        if (change)
        {
            BlockModification();
            PlaybackChanged();
            UnblockModification();
        }

        EndModification();
    }

    private void PlaybackChanged()
    {
        WasChanged = true;
        try
        {
            OnPlaybackChange?.Invoke(this);
        }
        catch (Exception ex)
        {
            Logging.Logger.Error($"SyncPlayback: playback change handler threw: {ex}");
        }
    }

    // ENCODING
    // The whole state every time. It is 18 bytes and it only moves on a transport change, so the
    // bit-diffing a streamed member needs would buy nothing here and cost a second encoding to keep
    // in step with the first.

    protected override void InternalEncodeFull(BinaryWriter writer, BinaryMessageBatch outboundMessage)
    {
        writer.Write(_playing);
        writer.Write((byte)_loop);
        writer.Write(_anchor);
        writer.Write(_anchorPosition);
        writer.Write(_speed);
    }

    protected override void InternalDecodeFull(BinaryReader reader, BinaryMessageBatch inboundMessage)
    {
        bool playing = reader.ReadBoolean();
        var loop = (PlaybackLoopMode)reader.ReadByte();
        double anchor = reader.ReadDouble();
        float anchorPosition = reader.ReadSingle();
        float speed = reader.ReadSingle();

        if (loop > PlaybackLoopMode.PingPong)
            loop = PlaybackLoopMode.Once;

        // The anchor is session-clock seconds and crosses raw: both peers already agree on that
        // timeline, so re-basing it here would apply the offset a second time.
        InternalSetState(playing, loop, anchor, anchorPosition, speed, sync: false);
    }

    protected override void InternalEncodeDelta(BinaryWriter writer, BinaryMessageBatch outboundMessage)
        => InternalEncodeFull(writer, outboundMessage);

    protected override void InternalDecodeDelta(BinaryReader reader, BinaryMessageBatch inboundMessage)
        => InternalDecodeFull(reader, inboundMessage);

    protected override void InternalClearDirty()
    {
    }

    // PERSISTENCE
    // The POSITION is saved, never the anchor: an anchor is an instant on a clock that has moved on by
    // the time the file is opened again, so restoring one literally replays however long the world sat
    // on disk. Saving where the head was and re-anchoring on load is what makes a saved world resume
    // instead of fast-forwarding. -xlinka

    public override DataTreeNode Save(SaveControl control)
    {
        var dictionary = new DataTreeDictionary();
        dictionary.Add("Playing", _playing);
        dictionary.Add("Loop", _loop.ToString());
        // Wrapped where there is a range to wrap into, so a clip that has been looping for an hour
        // saves a small number instead of one that has run out of float.
        dictionary.Add("Position", HasLength ? Position : RawPosition);
        dictionary.Add("Speed", _speed);
        return dictionary;
    }

    public override void Load(DataTreeNode node, LoadControl control)
    {
        if (node is not DataTreeDictionary dictionary)
            return;

        bool playing = dictionary.ExtractOrDefault("Playing", false);
        float position = dictionary.ExtractOrDefault("Position", 0f);
        float speed = dictionary.ExtractOrDefault("Speed", 1f);
        var loop = ParseLoopMode(dictionary.TryGetNode("Loop"), PlaybackLoopMode.Once);

        SetLoadedState(playing, loop, position, speed);
    }

    // Also the entry point for a worker migrating an older save shape onto this member.
    public void SetLoadedState(bool playing, PlaybackLoopMode loop, float position, float speed)
    {
        InternalSetState(
            playing,
            loop,
            NowSeconds,
            float.IsFinite(position) ? position : 0f,
            float.IsFinite(speed) ? speed : 1f,
            sync: false,
            change: false);
    }

    // Enums persist as their NAME, so an unknown one is a build that does not have that mode rather
    // than corrupt data: fall back instead of throwing the whole component's load away.
    public static PlaybackLoopMode ParseLoopMode(DataTreeNode? node, PlaybackLoopMode fallback)
    {
        if (node is not DataTreeValue { IsNull: false } value)
            return fallback;
        string? name = value.Extract<string>();
        if (string.IsNullOrEmpty(name))
            return fallback;
        return Enum.TryParse<PlaybackLoopMode>(name, out var parsed) ? parsed : fallback;
    }

    // The state is five plain values with no sync member of their own, so the generic duplication walk
    // has nothing to reach - without this a duplicated playback comes out stopped at zero.
    public void CopyFromSource(ISyncMember source, Action<ISyncMember, ISyncMember> copyChild)
    {
        if (source is not SyncPlayback other || ReferenceEquals(other, this))
            return;
        _length = other._length;
        InternalSetState(other._playing, other._loop, other._anchor, other._anchorPosition, other._speed);
    }

    public override void Dispose()
    {
        OnPlaybackChange = null;
        base.Dispose();
    }

    public override string ToString()
        => $"{(_playing ? "playing" : "paused")} {Position:0.###}s x{_speed:0.##} {_loop}";
}

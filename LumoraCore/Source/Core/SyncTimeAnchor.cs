// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.IO;
using Lumora.Core.Components.Utility;
using Lumora.Core.Networking;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Persistence;

namespace Lumora.Core;

// One shared instant: "this happened at X", where X is session-clock seconds.
//
// The point is that only the INSTANT replicates and every peer answers "how long ago" from its own
// clock. A cooldown, a countdown, a spawn time, a "last touched" stamp - all of them are one write
// when the thing happens and silence afterwards, instead of a number somebody has to keep sending
// while it counts. A peer joining mid-cooldown gets the right remaining time from the same one value.
//
// Storing the instant rather than an offset-from-now is what makes that work, and it only works
// because the session clock already agrees across peers: the correction is paid once, centrally, so
// the anchor crosses the wire raw and nobody has to re-base it on arrival. -xlinka
public class SyncTimeAnchor : ConflictingSyncElement, ISyncMemberCopy
{
    private double _anchor;
    private bool _set;

    public event Action<SyncTimeAnchor>? OnAnchorChange;

    private double NowSeconds => UtilityClock.Seconds(World);

    // Session-clock seconds. Reading an unset anchor gives the current instant, so Elapsed is zero
    // rather than however many seconds the session clock happens to be showing.
    public double Anchor
    {
        get => _set ? _anchor : NowSeconds;
        set => InternalSetState(value, true);
    }

    // False until somebody sets it. Separate from the value because zero is a legal instant.
    public bool IsSet => _set;

    // Seconds since the anchor. Negative while the anchor is still in the future, which is what makes
    // a scheduled start work without a second member.
    public double Elapsed => ElapsedAt(NowSeconds);

    public double ElapsedAt(double sessionSeconds) => _set ? sessionSeconds - _anchor : 0.0;

    public bool HasElapsed(double seconds) => _set && Elapsed >= seconds;

    // Seconds left until Elapsed reaches the given span, floored at zero.
    public double Remaining(double span)
    {
        if (!_set)
            return span;
        double left = span - Elapsed;
        return left > 0.0 ? left : 0.0;
    }

    public void SetNow() => InternalSetState(NowSeconds, true);

    public void SetIn(double seconds) => InternalSetState(NowSeconds + seconds, true);

    public void Clear() => InternalSetState(0.0, false);

    private void InternalSetState(double anchor, bool set, bool sync = true, bool change = true)
    {
        if (!double.IsFinite(anchor))
            anchor = 0.0;

        if (!BeginModification(throwOnError: false))
            return;

        _anchor = anchor;
        _set = set;

        if (sync)
            InvalidateSyncElement();

        if (change)
        {
            BlockModification();
            WasChanged = true;
            try
            {
                OnAnchorChange?.Invoke(this);
            }
            catch (Exception ex)
            {
                Logging.Logger.Error($"SyncTimeAnchor: anchor change handler threw: {ex}");
            }
            UnblockModification();
        }

        EndModification();
    }

    protected override void InternalEncodeFull(BinaryWriter writer, BinaryMessageBatch outboundMessage)
    {
        writer.Write(_set);
        writer.Write(_anchor);
    }

    protected override void InternalDecodeFull(BinaryReader reader, BinaryMessageBatch inboundMessage)
    {
        bool set = reader.ReadBoolean();
        double anchor = reader.ReadDouble();
        InternalSetState(anchor, set, sync: false);
    }

    protected override void InternalEncodeDelta(BinaryWriter writer, BinaryMessageBatch outboundMessage)
        => InternalEncodeFull(writer, outboundMessage);

    protected override void InternalDecodeDelta(BinaryReader reader, BinaryMessageBatch inboundMessage)
        => InternalDecodeFull(reader, inboundMessage);

    protected override void InternalClearDirty()
    {
    }

    // ELAPSED is saved, not the instant. An instant restored from a file a week later reads as a week
    // of elapsed time, which is true of the clock and useless to whatever asked - a cooldown does not
    // want to have expired because the world sat on disk. Saving the span and re-anchoring on load
    // keeps "how long ago" meaning what it meant when the file was written. -xlinka

    public override DataTreeNode Save(SaveControl control)
    {
        var dictionary = new DataTreeDictionary();
        dictionary.Add("Set", _set);
        dictionary.Add("Elapsed", _set ? Elapsed : 0.0);
        return dictionary;
    }

    public override void Load(DataTreeNode node, LoadControl control)
    {
        if (node is not DataTreeDictionary dictionary)
            return;
        bool set = dictionary.ExtractOrDefault("Set", false);
        double elapsed = dictionary.ExtractOrDefault("Elapsed", 0.0);
        InternalSetState(NowSeconds - elapsed, set, sync: false, change: false);
    }

    // Two plain values with no sync member of their own; the generic duplication walk cannot reach
    // them. The ELAPSED span is what carries over, so a duplicated cooldown is as far through as the
    // one it was copied from rather than restarting.
    public void CopyFromSource(ISyncMember source, Action<ISyncMember, ISyncMember> copyChild)
    {
        if (source is not SyncTimeAnchor other || ReferenceEquals(other, this))
            return;
        InternalSetState(other._anchor, other._set);
    }

    public override void Dispose()
    {
        OnAnchorChange = null;
        base.Dispose();
    }

    public override string ToString() => _set ? $"{Elapsed:0.###}s ago" : "<unset>";
}

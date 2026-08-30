// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Core.Networking.Sync;

public enum LinkSequenceVerdict
{
    Unsequenced,
    Apply,
    Duplicate,
    Gap
}

// Receive cursor for one link direction.
//
// Deltas are OPS, not values, so their order and completeness are load bearing: a peer that misses one
// and keeps applying the rest is wrong about that element forever and has no way to notice. Every
// delta therefore carries a per-link counter, and the receiver only applies the one that is exactly
// next. Anything ahead of that means batches were lost and the link needs full state, which is the only
// repair guaranteed to be correct regardless of what went missing.
//
// A FULL-WORLD batch supersedes everything up to its tag, so it moves the cursor. A PARTIAL full batch
// (drive corrections, a single-element resync) carries 0 and must not move it, or the deltas still in
// flight for every other element would be read as duplicates and thrown away. -xlinka
public sealed class LinkSequenceState
{
    // 0 means nothing has been applied yet.
    public ulong LastApplied;

    // Set on the authority when a peer's delta stream gapped: its deltas are refused until it confirms it has
    // rebased on the full state we pushed at it.
    public bool AwaitingResync;

    public int DuplicatesSinceLog;

    public bool LoggedDuplicate;

    public LinkSequenceVerdict Classify(ulong sequence)
    {
        if (sequence == 0)
            return LinkSequenceVerdict.Unsequenced;

        // Nothing applied yet: whatever arrives first defines the base. The join snapshot normally sets
        // the cursor before any delta lands, so this only covers a link that starts mid-stream, and
        // refusing there would desync every join for nothing.
        if (LastApplied == 0)
            return LinkSequenceVerdict.Apply;

        if (sequence <= LastApplied)
            return LinkSequenceVerdict.Duplicate;

        if (sequence == LastApplied + 1)
            return LinkSequenceVerdict.Apply;

        return LinkSequenceVerdict.Gap;
    }

    public ulong MissingBefore(ulong sequence)
        => sequence > LastApplied + 1 ? sequence - LastApplied - 1 : 0;

    public void Advance(ulong sequence)
    {
        if (sequence > LastApplied)
            LastApplied = sequence;
        LoggedDuplicate = false;
        DuplicatesSinceLog = 0;
    }

    public void AcceptFull(ulong tag)
    {
        if (tag > LastApplied)
            LastApplied = tag;
        AwaitingResync = false;
        LoggedDuplicate = false;
        DuplicatesSinceLog = 0;
    }

    // Used after a peer acknowledges it restarted from full state, since its own counter keeps running across
    // the repair.
    public void Rebase()
    {
        LastApplied = 0;
        AwaitingResync = false;
        LoggedDuplicate = false;
        DuplicatesSinceLog = 0;
    }
}

// Thrown by an incremental collection whose delta does not line up with what it holds locally.
//
// Incremental ops are only meaningful against the exact state they were computed from. When the
// sender's pre-op element count disagrees with ours the ops are meaningless, and applying them anyway
// is how a list ends up quietly holding the wrong things. The record is abandoned untouched and the
// session layer asks the authority to re-send this one element in full. -xlinka
public class ElementResyncRequiredException : Exception
{
    public RefID TargetID { get; }

    public ElementResyncRequiredException(RefID targetID, string message)
        : base(message)
    {
        TargetID = targetID;
    }
}

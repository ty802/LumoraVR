// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Persistence;

// What a component becomes when this build cannot resolve the type it was saved as.
//
// WHY IT EXISTS. Dropping the entry and logging a line is a data-destroying operation dressed up as
// error handling: the world still loads, the person saves it again five minutes later, and the parts
// this build did not recognise are now gone from their file forever. That is the whole cost of a
// removed plugin, a renamed class nobody added an alias for, or opening content built on a newer
// version. Holding the bytes costs nothing and keeps the file loadable by the build that does know
// what they mean.
//
// It is inert by construction: no update, no attach behaviour, no hook. It holds the original type
// name and the original member data, and it hands both straight back on save under the ORIGINAL type,
// so a load/save round trip of unrecognised content reproduces what was there.
//
// LIMIT worth knowing: references pointing INTO the preserved component (at it or at one of its
// members) still break, exactly as they do today, because there is nothing local to bind them to. Its
// own data survives; the graph edges into it do not.
//
// NEVER RENAME THIS TYPE. Its full name is written into save files as the placeholder's own tag on
// the paths that save it as itself, and a rename would strand them. -xlinka
[ComponentCategory(ComponentLibrary.HiddenCategory)]
public sealed class UnresolvedComponent : Component
{
    // The name exactly as the file spelled it. A real member so it shows up as one read-only row in
    // the inspector saying what is missing, rather than an anonymous blank component.
    public readonly Sync<string> MissingType = new();

    // The version the original file stamped for that type, carried so a re-save reproduces it.
    [HideInInspector]
    public readonly Sync<int> MissingTypeVersion = new();

    // Verbatim member data. Does not replicate - see SyncRawData. Hidden because there is nothing
    // useful to render: the whole point is that this build cannot interpret it.
    [HideInInspector]
    public readonly SyncRawData Data = new();

    public bool HasPreservedData => !Data.IsEmpty;

    // A placeholder with nothing in it is a peer's copy: the presence replicated, the payload did not.
    // Saving that would write an empty component over the real one, so it excludes itself from saves
    // instead and the peer's file simply lacks the component - which is what that peer would have
    // written before any of this existed. -xlinka
    public override bool IsPersistent => HasPreservedData && base.IsPersistent;

    public override string ToString()
        => $"Unresolved({(string.IsNullOrEmpty(MissingType.Value) ? "?" : MissingType.Value)})";
}

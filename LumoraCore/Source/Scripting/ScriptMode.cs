// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Scripting;

// Where a script runs.
public enum ScriptMode : byte
{
    // On the session host, as the script's author, writing replicated state through the gate.
    Shared,

    // On every peer, as its author. A person's changes only what it made on each machine; the world's also
    // changes what is in the world, as the user at each machine, the way that user's own edits do.
    Local,

    // On its author's own machine, as its author, writing replicated state the way the author's own edits do,
    // which the host judges as it judges theirs. Their input and their view are its to read. A world script's
    // author is the world, so one of these runs on the host like a Shared one.
    Author
}

// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components;

namespace Lumora.Core.Scripting;

// A script's batch in the history of the person at this machine, where it also ran: a Local script's, or
// a Shared script's whose owner is at the host. Undo and redo put it back here and now.
internal sealed class ScriptUndoLocalBatch : IUndoBatch
{
    private readonly ScriptUndoBatch _batch;

    public ScriptUndoLocalBatch(ScriptUndoBatch batch)
    {
        _batch = batch;
    }

    public string Description => _batch.Description;

    public bool Undo() => _batch.Apply(undo: true);

    public bool Redo() => _batch.Apply(undo: false);

    public void OnEvicted() => _batch.Evict();
}

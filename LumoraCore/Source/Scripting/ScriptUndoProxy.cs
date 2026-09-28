// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components;
using Lumora.Core.Components.Scripting;

namespace Lumora.Core.Scripting;

// A batch the host holds for the person at this machine, in their own history. Undo and redo ask the host
// to put it back; letting it go tells the host to let it go too.
internal sealed class ScriptUndoProxy : IUndoBatch
{
    private readonly ScriptUndoRelay _relay;

    public int Id { get; }

    public string Description { get; }

    public ScriptUndoProxy(ScriptUndoRelay relay, int id, string description)
    {
        _relay = relay;
        Id = id;
        Description = description;
    }

    public bool Undo() => _relay.Ask(Id, undone: true);

    public bool Redo() => _relay.Ask(Id, undone: false);

    public void OnEvicted() => _relay.LetGo(Id);
}

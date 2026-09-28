// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Scripting;

// One thing a script's undo nodes recorded: what its target held before the script changed it, and, once
// the batch closes, what the script left there. Undo and redo put one or the other back through the same
// writes a node makes, so the gate judges them as the change itself was judged. -xlinka
internal abstract class ScriptUndoStep
{
    // Takes what the target holds as the batch closes. False when that is what it held before, and the
    // step is left out: there is nothing to undo.
    public abstract bool Close();

    // False when the target is gone or the write was refused.
    public abstract bool Undo(ScriptContext ctx);

    public abstract bool Redo(ScriptContext ctx);

    // The step leaves every history for good. A slot it holds parked out of the world goes for real.
    public virtual void Evict()
    {
    }

    // Whether this records the same target as another step of the same kind: a batch keeps the first,
    // which holds what the target had before the batch began.
    public abstract bool Same(ScriptUndoStep other);

    // The name of a batch this step makes on its own.
    public abstract string Describe();
}

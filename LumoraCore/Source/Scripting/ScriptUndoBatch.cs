// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Warden;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Scripting;

// The steps one undo batch of a script holds, undone and redone as one. It is put back as the script's
// principal, with the rights the change was made with, through the same writes and the same gate: a Shared
// script's on the host, a Local script's on the machine that ran it, where nothing needs the gate. A step
// whose target is gone or refused is skipped; the batch holds while any part of it applies, and once none
// does, or its author has left, it is refused and drops like any other step whose targets are gone. -xlinka
internal sealed class ScriptUndoBatch
{
    private readonly List<ScriptUndoStep> _steps = new();

    public string Description { get; }
    public ScriptPrincipal Principal { get; }
    public World World { get; }
    public bool IsLocal { get; }
    public bool OnAuthorMachine { get; }

    // The script's own slot, where a Local script parks what it made.
    public Slot? Self { get; }

    public bool IsUndone { get; private set; }

    public int Count => _steps.Count;

    public ScriptUndoBatch(ScriptContext ctx, string description)
    {
        Description = description;
        Principal = ctx.Principal;
        World = ctx.World;
        IsLocal = ctx.IsLocal;
        OnAuthorMachine = ctx.OnAuthorMachine;
        Self = ctx.Self;
    }

    public bool Holds(ScriptUndoStep step)
    {
        foreach (var held in _steps)
        {
            if (held.Same(step))
                return true;
        }
        return false;
    }

    public void Add(ScriptUndoStep step) => _steps.Add(step);

    // What each target holds now becomes its redo. False when no step changed anything.
    public bool Close()
    {
        _steps.RemoveAll(static step => !step.Close());
        return _steps.Count > 0;
    }

    public bool Apply(bool undo)
    {
        if (World.IsDisposed || World.IsDestroyed || !Principal.IsStillAuthorIn(World))
            return false;
        // Never inside a scope that opens the gate: the batch would be put back with that scope's rights.
        if (PermissionEngine.IsGateOpenOnThisThread || WorldConfigEdit.IsOpen)
            return false;

        var ctx = new ScriptContext(World, Principal, Self, IsLocal, OnAuthorMachine);
        bool any = false;
        using (IsLocal && !Principal.IsWorldContent ? null : ctx.EnterWriter())
        {
            if (undo)
            {
                for (int i = _steps.Count - 1; i >= 0; i--)
                    any |= Try(_steps[i], ctx, undo: true);
            }
            else
            {
                for (int i = 0; i < _steps.Count; i++)
                    any |= Try(_steps[i], ctx, undo: false);
            }
        }
        if (any)
            IsUndone = undo;
        return any;
    }

    // The batch leaves every history for good.
    public void Evict()
    {
        foreach (var step in _steps)
        {
            try
            {
                step.Evict();
            }
            catch (Exception ex)
            {
                LumoraLogger.Warn($"Loom: letting go of an undo step failed: {ex.Message}");
            }
        }
        _steps.Clear();
    }

    private static bool Try(ScriptUndoStep step, ScriptContext ctx, bool undo)
    {
        try
        {
            return undo ? step.Undo(ctx) : step.Redo(ctx);
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (Exception ex)
        {
            LumoraLogger.Warn($"Loom: an undo step could not be put back: {ex.Message}");
            return false;
        }
    }
}

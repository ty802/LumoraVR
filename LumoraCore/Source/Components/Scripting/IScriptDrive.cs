// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Scripting;

// A link a running script holds one of its drive nodes with: whose it is, which node, where it points, and
// a value to show there on this machine. Only the host judges whether the author may hold the field, so it
// says what it found with Confirm, and every other machine shows nothing until it has. Show answers false
// while the link may not show.
public interface IScriptDrive
{
    ScriptHost? Owner { get; set; }
    string NodeId { get; set; }
    bool IsGone { get; }
    void Aim(IWorldElement? target);
    // Where a holding drive tells what it refused; nothing for one that does not hold.
    void Report(IWorldElement? heard, IWorldElement? heardCount);
    void Confirm();
    bool Show(object? value);
}

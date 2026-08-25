// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Variables;

// A component that takes part in a named variable: a declaration that holds the value, a field
// binding that borrows one, or a driver that only consumes it.
public interface IVariableParticipant : IWorldElement
{
    // "Scope/Name" or just "Name".
    string? VariablePath { get; }

    // Force the scope to be resolved again on the next changes pass. Called when the hierarchy above
    // moved, a scope was renamed, or a scope went away.
    void MarkScopeDirty();

    // Re-resolve scope and registration right now. Returns true when the registration actually moved.
    bool RefreshBinding();
}

// The typed half. A participant is either READABLE (it supplies the variable's value and takes
// writes back) or WRITE-ONLY (a driver: it never supplies a value, it only receives one).
public interface IVariableParticipant<T> : IVariableParticipant
{
    // Write-only participants are excluded from the readable count, so a variable with nothing but
    // drivers on it counts as absent and every driver falls back to its default.
    bool IsWriteOnly { get; }

    // On binding to a scope that already has a value for this identity, true replaces that value
    // with this participant's own instead of adopting it.
    bool OverridesOnBind { get; }

    // False for a field binding whose target is read-only or already driven.
    bool AcceptsWrites { get; }

    // The setter is how the manager broadcasts.
    T VariableValue { get; set; }
}

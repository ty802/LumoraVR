// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core;

/// <summary>
/// Interface for elements that have an initialization phase.
/// </summary>
public interface IInitializable
{
    /// <summary>
    /// Whether this element is currently in its initialization phase.
    /// During init phase, certain restrictions (like drive guards) are relaxed.
    /// </summary>
    bool IsInInitPhase { get; }

    /// <summary>
    /// End the initialization phase for this element.
    /// </summary>
    void EndInitPhase();
}
// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Avatar;

/// <summary>
/// The rendering context in which a RenderTransformOverride is active.
///</summary>
public enum RenderingContext
{
    /// <summary>Apply in every context.</summary>
    Any,

    /// <summary>Apply only when rendering from the local user's own camera view.</summary>
    UserView,

    /// <summary>Apply only when rendering into a mirror.</summary>
    Mirror,

    /// <summary>Apply only when rendering from a world Camera component.</summary>
    Camera,
}

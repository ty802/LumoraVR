// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components;

/// <summary>
/// Null locomotion module (no movement) used when locomotion is suppressed or unavailable.
/// </summary>
public class NullLocomotionModule : ILocomotionModule
{
    public void Activate(LocomotionController owner) { }
    public void Deactivate() { }
    public void Update(float delta) { }
    public void Dispose() { }
}
// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components;

/// <summary>
/// Placeholder permissions class for locomotion (stubbed to allow all).
/// </summary>
public class LocomotionPermissions
{
    public bool CanUseLocomotion(ILocomotionModule module) => true;
    public bool CanUseAnyLocomotion() => true;
}
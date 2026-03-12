// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components;

/// <summary>
/// Hook interface for CharacterController.
/// Implemented by platform-specific hooks (e.g. CharacterControllerHook in LumoraGodot).
/// </summary>
public interface ICharacterControllerHook : IHook
{
    void AddColliderShape(Collider collider);
    void RemoveColliderShape(Collider collider);
    void SetMovementDirection(float3 direction);
    void RequestJump();
    void SetCrouching(bool crouching);
    void Teleport(float3 position);
    bool IsOnFloor();
}

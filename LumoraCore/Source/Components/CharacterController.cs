// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core;
using Lumora.Core.Math;
using Lumora.Core.Physics;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components;

/// <summary>
/// Physics-based character controller for user movement.
/// Implements IColliderOwner pattern for collision management.
/// Physics behaviour is delegated to ICharacterControllerHook (e.g. CharacterControllerHook in LumoraGodot).
/// </summary>
[ComponentCategory("Physics")]
public class CharacterController : ImplementableComponent, IColliderOwner
{
    // ===== MOVEMENT PARAMETERS =====

    public float Speed { get; set; } = 5.0f;
    public float AirSpeed { get; set; } = 1.0f;
    public float JumpSpeed { get; set; } = 6.0f;
    public float CrouchSpeed { get; set; } = 2.5f;
    public float SprintMultiplier { get; set; } = 1.7f;
    public float StandingHeight { get; set; } = 1.8f;
    public float CrouchHeight { get; set; } = 1.0f;
    public float CrouchTransitionSpeed { get; set; } = 8.0f;

    // ===== STATE =====

    private readonly List<Collider> _colliders = new List<Collider>();
    private bool _isReady = false;
    private bool _isCrouching = false;
    private bool _isSprinting = false;
    private float3 _velocity = float3.Zero;

    private UserRoot _userRoot;

    public enum MovementState { Grounded, InAir }
    private MovementState _currentState = MovementState.InAir;

    private ICharacterControllerHook CharacterHook => Hook as ICharacterControllerHook;

    // ===== IColliderOwner IMPLEMENTATION =====

    public bool Kinematic => false;

    public void OnColliderAdded(Collider collider)
    {
        if (!_colliders.Contains(collider))
        {
            _colliders.Add(collider);
            LumoraLogger.Log($"CharacterController: Added collider {collider.GetType().Name}");
        }
    }

    public void OnColliderRemoved(Collider collider)
    {
        if (_colliders.Remove(collider))
        {
            CharacterHook?.RemoveColliderShape(collider);
            LumoraLogger.Log($"CharacterController: Removed collider {collider.GetType().Name}");
        }
    }

    public void OnColliderShapeChanged(Collider collider)
    {
        // Hook rebuilds the shape on its next ApplyChanges call
    }

    public void PostprocessBoundsOffset(ref float3 offset) { }

    // ===== INITIALIZATION =====

    public override void OnAwake()
    {
        base.OnAwake();
        _userRoot = Slot.GetComponent<UserRoot>();
        if (_userRoot == null)
            LumoraLogger.Warn("CharacterController: No UserRoot found!");
    }

    public override void OnStart()
    {
        base.OnStart();
        TryInitializeLocalUser();
    }

    private void DiscoverColliders()
    {
        _colliders.Clear();
        foreach (var component in Slot.Components)
        {
            if (component is Collider collider &&
                collider.Enabled &&
                collider.Type.Value == ColliderType.CharacterController)
            {
                _colliders.Add(collider);
                LumoraLogger.Log($"CharacterController: Found {collider.GetType().Name} with Type=CharacterController");
            }
        }

        if (_colliders.Count > 1)
            LumoraLogger.Warn($"CharacterController: Found {_colliders.Count} colliders, using first one");

        LumoraLogger.Log($"CharacterController: Discovered {_colliders.Count} colliders in same slot");
    }

    public IReadOnlyList<Collider> GetColliders() => _colliders;

    // ===== UPDATE =====

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);

        if (!_isReady)
            TryInitializeLocalUser();

        if (!(_userRoot?.IsLocalUserRoot ?? false))
            return;

        if (_isReady && CharacterHook != null)
            RunApplyChanges();
    }

    private void TryInitializeLocalUser()
    {
        if (_isReady)
            return;

        if (_userRoot == null)
        {
            _userRoot = Slot.GetComponent<UserRoot>();
            if (_userRoot == null)
                return;
        }

        if (!_userRoot.IsLocalUserRoot)
            return;

        DiscoverColliders();

        if (CharacterHook != null)
        {
            foreach (var collider in _colliders)
                CharacterHook.AddColliderShape(collider);

            RunApplyChanges();
            LumoraLogger.Log("CharacterController: Physics enabled!");
        }
        else
        {
            LumoraLogger.Warn("CharacterController: CharacterHook is null!");
        }

        _isReady = true;
        LumoraLogger.Log($"CharacterController: Initialized for local user '{_userRoot.ActiveUser.UserName.Value}' with {_colliders.Count} colliders");
    }

    // ===== PUBLIC API (Called by LocomotionController) =====

    public bool IsReady => _isReady;

    public void SetMovementDirection(float3 direction)
    {
        _velocity = new float3(direction.x, _velocity.y, direction.z);
        CharacterHook?.SetMovementDirection(direction);
    }

    public void RequestJump()
    {
        CharacterHook?.RequestJump();
    }

    public void SetCrouching(bool crouching)
    {
        _isCrouching = crouching;
        if (crouching)
            _isSprinting = false;
        CharacterHook?.SetCrouching(crouching);
    }

    public void SetSprinting(bool sprinting)
    {
        if (_isCrouching)
        {
            _isSprinting = false;
            return;
        }
        _isSprinting = sprinting;
    }

    public bool IsCrouching => _isCrouching;
    public bool IsSprinting => _isSprinting;

    public float GetCurrentSpeed()
    {
        if (_isCrouching) return CrouchSpeed;
        if (_currentState == MovementState.InAir) return AirSpeed;
        return _isSprinting ? Speed * SprintMultiplier : Speed;
    }

    public void Teleport(float3 position)
    {
        if (CharacterHook != null)
            CharacterHook.Teleport(position);
        else
            Slot.GlobalPosition = position;
    }

    public bool IsGrounded()
    {
        if (CharacterHook != null)
            return CharacterHook.IsOnFloor();
        return _currentState == MovementState.Grounded;
    }

    public float3 GetVelocity() => _velocity;

    // ===== CLEANUP =====

    public override void OnDestroy()
    {
        _colliders.Clear();
        base.OnDestroy();
        LumoraLogger.Log("CharacterController: Destroyed");
    }
}

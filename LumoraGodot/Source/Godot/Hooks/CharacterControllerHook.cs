using Godot;
using Lumora.Core.Components;
using Lumora.Core.Math;
using System.Collections.Generic;
using AquaLogger = Lumora.Core.Logging.Logger;

namespace Aquamarine.Godot.Hooks;

/// <summary>
/// Hook for CharacterController component → Godot CharacterBody3D.
/// Platform physics hook for Godot.
/// </summary>
public class CharacterControllerHook : ComponentHook<CharacterController>
{
	private CharacterBody3D _characterBody;
	private Dictionary<object, CollisionShape3D> _collisionShapes = new Dictionary<object, CollisionShape3D>();
	private Vector3 _velocity;
	private Vector3 _moveDirection;
	private bool _jumpRequested;

	public CharacterBody3D GodotCharacterBody => _characterBody;

	public override void Initialize()
	{
		base.Initialize();

		AquaLogger.Log($"CharacterControllerHook: Initialize called, attachedNode={attachedNode != null}");

		_characterBody = new CharacterBody3D();
		_characterBody.Name = "CharacterController";
		attachedNode.AddChild(_characterBody);

		_characterBody.FloorStopOnSlope = true;
		_characterBody.FloorMaxAngle = Mathf.DegToRad(45f);
		_characterBody.WallMinSlideAngle = Mathf.DegToRad(15f);

		AquaLogger.Log($"CharacterControllerHook: CharacterBody3D created and added to scene tree (IsInsideTree={_characterBody.IsInsideTree()})");
	}

	public override void ApplyChanges()
	{
		if (_characterBody == null || !_characterBody.IsInsideTree())
			return;

		float delta = (float)Engine.GetPhysicsInterpolationFraction();

		// Apply gravity
		if (_characterBody.IsOnFloor())
		{
			// Clamp downward velocity to prevent bouncing
			if (_velocity.Y < 0)
				_velocity.Y = 0;
		}
		else
		{
			// Apply gravity
			_velocity.Y -= 9.81f * delta * 2.0f; // 2x gravity for snappier feel
		}

		// Apply movement
		if (_moveDirection.LengthSquared() > 0.001f)
		{
			float speed = _characterBody.IsOnFloor() ? Owner.Speed : Owner.AirSpeed;
			_velocity.X = _moveDirection.X * speed;
			_velocity.Z = _moveDirection.Z * speed;
		}
		else
		{
			// Decelerate to stop
			_velocity.X = Mathf.MoveToward(_velocity.X, 0, Owner.Speed * delta * 10);
			_velocity.Z = Mathf.MoveToward(_velocity.Z, 0, Owner.Speed * delta * 10);
		}

		// Apply jump
		if (_jumpRequested && _characterBody.IsOnFloor())
		{
			_velocity.Y = Owner.JumpSpeed;
			_jumpRequested = false;
			GD.Print("CharacterControllerHook: Jump!");
		}

		// Move character
		_characterBody.Velocity = _velocity;
		_characterBody.MoveAndSlide();
		_velocity = _characterBody.Velocity;

		// Sync position back to slot
		Owner.Slot.GlobalPosition = new float3(
			_characterBody.GlobalPosition.X,
			_characterBody.GlobalPosition.Y,
			_characterBody.GlobalPosition.Z
		);
	}

	public void SetMovementDirection(float3 direction)
	{
		_moveDirection = new Vector3(direction.x, direction.y, direction.z);
	}

	public void RequestJump()
	{
		_jumpRequested = true;
	}

	public void Teleport(float3 position)
	{
		if (_characterBody != null)
		{
			_characterBody.GlobalPosition = new Vector3(position.x, position.y, position.z);
			_velocity = Vector3.Zero;
		}
	}

	public bool IsOnFloor()
	{
		return _characterBody != null && _characterBody.IsOnFloor();
	}

	public void AddColliderShape(object collider)
	{
		AquaLogger.Log($"CharacterControllerHook: AddColliderShape called for {collider?.GetType().Name ?? "null"}");

		if (_collisionShapes.ContainsKey(collider))
		{
			AquaLogger.Log("CharacterControllerHook: Collider already has a shape, skipping");
			return;
		}

		if (_characterBody == null)
		{
			AquaLogger.Error("CharacterControllerHook: _characterBody is null!");
			return;
		}

		CollisionShape3D collisionShape = new CollisionShape3D();
		collisionShape.Name = $"Shape_{collider.GetType().Name}";
		AquaLogger.Log($"CharacterControllerHook: Created CollisionShape3D named '{collisionShape.Name}'");

		if (collider is BoxCollider boxCollider)
		{
			BoxShape3D boxShape = new BoxShape3D();
			float3 size = boxCollider.Size.Value;
			boxShape.Size = new Vector3(size.x, size.y, size.z);
			collisionShape.Shape = boxShape;
			AquaLogger.Log($"CharacterControllerHook: Created BoxShape3D with size {size}");
		}
		else if (collider is CapsuleCollider capsuleCollider)
		{
			CapsuleShape3D capsuleShape = new CapsuleShape3D();
			capsuleShape.Height = capsuleCollider.Height.Value;
			capsuleShape.Radius = capsuleCollider.Radius.Value;
			collisionShape.Shape = capsuleShape;
			AquaLogger.Log($"CharacterControllerHook: Created CapsuleShape3D with Height={capsuleShape.Height}, Radius={capsuleShape.Radius}");
		}
		else if (collider is SphereCollider sphereCollider)
		{
			SphereShape3D sphereShape = new SphereShape3D();
			sphereShape.Radius = sphereCollider.Radius.Value;
			collisionShape.Shape = sphereShape;
			AquaLogger.Log($"CharacterControllerHook: Created SphereShape3D with Radius={sphereShape.Radius}");
		}
		else
		{
			AquaLogger.Warn($"CharacterControllerHook: Unknown collider type {collider.GetType().Name}");
		}

		AquaLogger.Log($"CharacterControllerHook: Adding collision shape to CharacterBody3D (IsInsideTree={_characterBody.IsInsideTree()})");
		_characterBody.AddChild(collisionShape);
		_collisionShapes[collider] = collisionShape;

		AquaLogger.Log($"CharacterControllerHook: Successfully added {collisionShape.Name} to CharacterBody3D");
	}

	public void RemoveColliderShape(object collider)
	{
		if (_collisionShapes.TryGetValue(collider, out CollisionShape3D shape))
		{
			shape.QueueFree();
			_collisionShapes.Remove(collider);
		}
	}

	public override void Destroy(bool destroyingWorld)
	{
		if (!destroyingWorld && _characterBody != null && GodotObject.IsInstanceValid(_characterBody))
		{
			_characterBody.QueueFree();
		}

		_characterBody = null;
		_collisionShapes.Clear();

		base.Destroy(destroyingWorld);
	}
}

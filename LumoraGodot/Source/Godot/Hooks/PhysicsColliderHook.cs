using Godot;
using Lumora.Core.Components;
using Lumora.Core.Math;
using Lumora.Core.Physics;
using AquaLogger = Lumora.Core.Logging.Logger;

namespace Aquamarine.Godot.Hooks
{
	/// <summary>
	/// Shared hook for BoxCollider, CapsuleCollider, and SphereCollider -> Godot physics bodies.
	/// Creates a StaticBody3D or RigidBody3D with a CollisionShape3D and keeps it synced to the slot.
	/// CharacterController colliders are handled separately by CharacterControllerHook.
	/// </summary>
	public class PhysicsColliderHook : ComponentHook<Collider>
	{
		private Node3D _bodyNode;
		private CollisionShape3D _collisionShape;
		private Shape3D _shape;
		private bool _isDynamic;
		private PhysicsMaterial _material;

		public override void Initialize()
		{
			base.Initialize();

			// Character controller colliders are handled by CharacterControllerHook
			if (Owner.Type.Value == ColliderType.CharacterController)
				return;

			CreateBody();
			BuildShape();
			UpdateTransform();
		}

		public override void ApplyChanges()
		{
			if (Owner.Type.Value == ColliderType.CharacterController)
				return;

			if (_bodyNode == null || !GodotObject.IsInstanceValid(_bodyNode))
				return;

			// Recreate body if dynamic/static state changed
			bool shouldBeDynamic = Owner.Mass.Value > 0.0001f && Owner.Type.Value != ColliderType.Static;
			if (shouldBeDynamic != _isDynamic)
			{
				DestroyBody(false);
				CreateBody();
				BuildShape();
			}

			// Update shape parameters
			BuildShape();

			// Sync transform from slot
			UpdateTransform();

			// Enable/disable collision
			_bodyNode.Visible = Owner.Enabled;
			if (_bodyNode is CollisionObject3D co)
			{
				co.CollisionLayer = Owner.Enabled ? 1u : 0u;
				co.CollisionMask = Owner.Enabled ? 1u : 0u;
			}
		}

		private void CreateBody()
		{
			_isDynamic = Owner.Mass.Value > 0.0001f && Owner.Type.Value != ColliderType.Static;

			if (_isDynamic)
			{
				_material = new PhysicsMaterial { Friction = 1f, Bounce = 0f };
				var rigid = new RigidBody3D
				{
					Name = "RigidCollider",
					PhysicsMaterialOverride = _material,
					Mass = Owner.Mass.Value
				};
				_bodyNode = rigid;
			}
			else
			{
				_material = new PhysicsMaterial { Friction = 1f, Bounce = 0f };
				var staticBody = new StaticBody3D
				{
					Name = "StaticCollider",
					PhysicsMaterialOverride = _material
				};
				_bodyNode = staticBody;
			}

			_collisionShape = new CollisionShape3D { Name = "Shape" };
			_bodyNode.AddChild(_collisionShape);

			// Parent under world root to avoid double transforms
			Node3D worldRoot = Owner?.World?.GodotSceneRoot as Node3D;
			Node parentNode = (Node)worldRoot ?? attachedNode;
			parentNode.AddChild(_bodyNode);
		}

		private void BuildShape()
		{
			if (_collisionShape == null || !GodotObject.IsInstanceValid(_collisionShape))
				return;

			Shape3D newShape = _shape;

			switch (Owner)
			{
				case BoxCollider box:
					var boxShape = new BoxShape3D();
					float3 size = box.Size.Value;
					boxShape.Size = new Vector3(size.x, size.y, size.z);
					newShape = boxShape;
					break;
				case CapsuleCollider capsule:
					var cap = new CapsuleShape3D();
					cap.Radius = capsule.Radius.Value;
					cap.Height = capsule.Height.Value;
					newShape = cap;
					break;
				case SphereCollider sphere:
					var sph = new SphereShape3D();
					sph.Radius = sphere.Radius.Value;
					newShape = sph;
					break;
				default:
					AquaLogger.Warn($"PhysicsColliderHook: Unknown collider type {Owner.GetType().Name}");
					return;
			}

			if (_shape != newShape)
			{
				_shape = newShape;
				_collisionShape.Shape = _shape;
			}

			// Apply offset
			var offset = Owner.Offset.Value;
			_collisionShape.Position = new Vector3(offset.x, offset.y, offset.z);
		}

		private void UpdateTransform()
		{
			if (_bodyNode == null || !GodotObject.IsInstanceValid(_bodyNode))
				return;

			var slotNode = slotHook?.GeneratedNode3D;
			if (slotNode != null && slotNode.IsInsideTree())
			{
				_bodyNode.GlobalTransform = slotNode.GlobalTransform;
			}
		}

		public override void Destroy(bool destroyingWorld)
		{
			DestroyBody(!destroyingWorld);
			base.Destroy(destroyingWorld);
		}

		private void DestroyBody(bool queueFree)
		{
			if (_collisionShape != null && GodotObject.IsInstanceValid(_collisionShape) && queueFree)
			{
				_collisionShape.QueueFree();
			}

			if (_bodyNode != null && GodotObject.IsInstanceValid(_bodyNode) && queueFree)
			{
				_bodyNode.QueueFree();
			}

			_collisionShape = null;
			_bodyNode = null;
			_shape = null;
		}
	}
}

using Godot;

namespace Aquamarine.Source.Core.Components;

/// <summary>
/// Component that adds physics collision to a Slot.
/// </summary>
public partial class ColliderComponent : Component
{
	public enum ColliderType
	{
		Box,
		Sphere,
		Capsule,
		Mesh
	}

	private CollisionShape3D _collisionShape;
	private StaticBody3D _body;

	/// <summary>
	/// Type of collider shape (synchronized).
	/// </summary>
	public Sync<ColliderType> ShapeType { get; private set; }

	/// <summary>
	/// Size/dimensions of the collider (synchronized).
	/// </summary>
	public Sync<Vector3> Size { get; private set; }

	/// <summary>
	/// Whether this is a trigger (no physics response).
	/// </summary>
	public Sync<bool> IsTrigger { get; private set; }

	public override string ComponentName => "Collider";

	public ColliderComponent()
	{
		ShapeType = new Sync<ColliderType>(this, ColliderType.Box);
		Size = new Sync<Vector3>(this, Vector3.One);
		IsTrigger = new Sync<bool>(this, false);

		ShapeType.OnChanged += UpdateShape;
		Size.OnChanged += UpdateSize;
	}

	public override void OnAwake()
	{
		_body = new StaticBody3D();
		_body.Name = "StaticBody";
		_collisionShape = new CollisionShape3D();
		_collisionShape.Name = "CollisionShape";

		Slot?.AddChild(_body);
		_body.AddChild(_collisionShape);

		UpdateShape(ShapeType.Value);
	}

	private void UpdateShape(ColliderType type)
	{
		if (_collisionShape == null) return;

		Shape3D shape = type switch
		{
			ColliderType.Box => new BoxShape3D(),
			ColliderType.Sphere => new SphereShape3D(),
			ColliderType.Capsule => new CapsuleShape3D(),
			_ => new BoxShape3D()
		};

		_collisionShape.Shape = shape;
		UpdateSize(Size.Value);
	}

	private void UpdateSize(Vector3 size)
	{
		if (_collisionShape?.Shape == null) return;

		switch (_collisionShape.Shape)
		{
			case BoxShape3D box:
				box.Size = size;
				break;
			case SphereShape3D sphere:
				sphere.Radius = size.X;
				break;
			case CapsuleShape3D capsule:
				capsule.Radius = size.X;
				capsule.Height = size.Y;
				break;
		}
	}

	public override void OnDestroy()
	{
		_collisionShape?.QueueFree();
		_body?.QueueFree();
		base.OnDestroy();
	}
}

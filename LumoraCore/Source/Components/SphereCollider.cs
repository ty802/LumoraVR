using Lumora.Core;
using Lumora.Core.Math;
using Lumora.Core.Physics;
using AquaLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components;

/// <summary>
/// Sphere-shaped collider.
/// </summary>
[ComponentCategory("Physics/Colliders")]
public class SphereCollider : ImplementableComponent
{
	// ===== SYNC FIELDS =====

	public readonly Sync<float> Radius;
	public readonly Sync<float3> Offset;

	// ===== INITIALIZATION =====

	public SphereCollider()
	{
		Radius = new Sync<float>(this, 0.5f);
		Offset = new Sync<float3>(this, float3.Zero);
	}

	public override void OnAwake()
	{
		base.OnAwake();
		AquaLogger.Log($"SphereCollider: Initialized with Radius={Radius.Value}");
	}

}

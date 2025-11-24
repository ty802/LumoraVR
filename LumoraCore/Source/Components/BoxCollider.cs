using Lumora.Core;
using Lumora.Core.Math;
using Lumora.Core.Physics;
using AquaLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components;

/// <summary>
/// Box-shaped collider (rectangular prism).
/// </summary>
[ComponentCategory("Physics/Colliders")]
public class BoxCollider : ImplementableComponent
{
	// ===== SYNC FIELDS =====

	public readonly Sync<float3> Size;
	public readonly Sync<float3> Offset;

	// ===== INITIALIZATION =====

	public BoxCollider()
	{
		Size = new Sync<float3>(this, float3.One);
		Offset = new Sync<float3>(this, float3.Zero);
	}

	public override void OnAwake()
	{
		base.OnAwake();
		AquaLogger.Log($"BoxCollider: Initialized with Size={Size.Value}");
	}

}

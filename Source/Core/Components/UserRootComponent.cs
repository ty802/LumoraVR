using Godot;
using AquaLogger = Aquamarine.Source.Logging.Logger;

namespace Aquamarine.Source.Core.Components;

/// <summary>
/// UserRoot component - represents a single user's presence in the world.
/// Each user gets their own UserRoot component that links to their User data object.
/// </summary>
public partial class UserRootComponent : Component
{
	/// <summary>
	/// The User data object this UserRoot represents.
	/// </summary>
	public User TargetUser { get; set; }

	public UserRootComponent()
	{
	}

	public override void OnAwake()
	{
		base.OnAwake();
		AquaLogger.Log($"UserRootComponent initialized on {Slot?.SlotName.Value ?? "unknown slot"}");
	}

	public override void OnUpdate(float delta)
	{
		// UserRoot is passive - avatar and character controller handle updates
	}

	public override void OnDestroy()
	{
		base.OnDestroy();
	}
}

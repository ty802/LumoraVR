using Godot;

namespace Aquamarine.Source.Core;

/// <summary>
/// Base interface for all elements that exist within a World.
/// 
/// </summary>
public interface IWorldElement
{
	/// <summary>
	/// The World this element belongs to.
	/// </summary>
	World World { get; }

	/// <summary>
	/// Unique reference ID for this element within the world.
	/// </summary>
	ulong RefID { get; }

	/// <summary>
	/// Whether this element has been destroyed.
	/// </summary>
	bool IsDestroyed { get; }

	/// <summary>
	/// Whether this element has been initialized.
	/// </summary>
	bool IsInitialized { get; }

	/// <summary>
	/// Destroy this element and remove it from the world.
	/// </summary>
	void Destroy();
}

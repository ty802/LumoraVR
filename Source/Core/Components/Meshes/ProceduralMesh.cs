using Godot;

namespace Aquamarine.Source.Core.Components.Meshes;

/// <summary>
/// Base class for procedural meshes that can be generated at runtime.
/// </summary>
public abstract class ProceduralMesh
{
	/// <summary>
	/// Generate the Godot mesh with the current parameters.
	/// </summary>
	public abstract Mesh Generate();

	/// <summary>
	/// Get a hash of the mesh parameters for change detection.
	/// </summary>
	public abstract int GetParameterHash();
}

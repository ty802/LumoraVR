using System;
using Godot;

namespace Aquamarine.Source.Core.Components.Meshes;

/// <summary>
/// Procedural sphere mesh.
/// </summary>
public class SphereMesh : ProceduralMesh
{
	public float Radius { get; set; } = 0.5f;
	public float Height { get; set; } = 1.0f;
	public int RadialSegments { get; set; } = 64;
	public int Rings { get; set; } = 32;
	public bool IsHemisphere { get; set; } = false;

	public override Mesh Generate()
	{
		var mesh = new Godot.SphereMesh
		{
			Radius = Radius,
			Height = Height,
			RadialSegments = RadialSegments,
			Rings = Rings,
			IsHemisphere = IsHemisphere
		};

		return mesh;
	}

	public override int GetParameterHash()
	{
		return HashCode.Combine(Radius, Height, RadialSegments, Rings, IsHemisphere);
	}
}

using System;
using Godot;

namespace Aquamarine.Source.Core.Components.Meshes;

/// <summary>
/// Procedural cylinder mesh.
/// </summary>
public class CylinderMesh : ProceduralMesh
{
	public float TopRadius { get; set; } = 0.5f;
	public float BottomRadius { get; set; } = 0.5f;
	public float Height { get; set; } = 2.0f;
	public int RadialSegments { get; set; } = 64;
	public int Rings { get; set; } = 4;
	public bool CapTop { get; set; } = true;
	public bool CapBottom { get; set; } = true;

	public override Mesh Generate()
	{
		var mesh = new Godot.CylinderMesh
		{
			TopRadius = TopRadius,
			BottomRadius = BottomRadius,
			Height = Height,
			RadialSegments = RadialSegments,
			Rings = Rings,
			CapTop = CapTop,
			CapBottom = CapBottom
		};

		return mesh;
	}

	public override int GetParameterHash()
	{
		return HashCode.Combine(TopRadius, BottomRadius, Height, RadialSegments, Rings, CapTop, CapBottom);
	}
}

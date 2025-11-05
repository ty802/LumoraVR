using System;
using Godot;

namespace Aquamarine.Source.Core.Components.Meshes;

/// <summary>
/// Procedural box/cube mesh.
/// </summary>
public class BoxMesh : ProceduralMesh
{
	public Vector3 Size { get; set; } = Vector3.One;
	public int SubdivideWidth { get; set; } = 0;
	public int SubdivideHeight { get; set; } = 0;
	public int SubdivideDepth { get; set; } = 0;

	public override Mesh Generate()
	{
		var mesh = new Godot.BoxMesh
		{
			Size = Size,
			SubdivideWidth = SubdivideWidth,
			SubdivideHeight = SubdivideHeight,
			SubdivideDepth = SubdivideDepth
		};

		return mesh;
	}

	public override int GetParameterHash()
	{
		return HashCode.Combine(Size, SubdivideWidth, SubdivideHeight, SubdivideDepth);
	}
}

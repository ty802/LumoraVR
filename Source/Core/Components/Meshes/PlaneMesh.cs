using System;
using Godot;

namespace Aquamarine.Source.Core.Components.Meshes;

/// <summary>
/// Procedural plane/quad mesh.
/// </summary>
public class PlaneMesh : ProceduralMesh
{
	public Vector2 Size { get; set; } = Vector2.One;
	public int SubdivideWidth { get; set; } = 0;
	public int SubdivideDepth { get; set; } = 0;
	public Vector3 CenterOffset { get; set; } = Vector3.Zero;
	public PlaneOrientation Facing { get; set; } = PlaneOrientation.FaceY;

	public enum PlaneOrientation
	{
		FaceX,
		FaceY,
		FaceZ
	}

	public override Mesh Generate()
	{
		var mesh = new Godot.PlaneMesh
		{
			Size = Size,
			SubdivideWidth = SubdivideWidth,
			SubdivideDepth = SubdivideDepth,
			CenterOffset = CenterOffset,
			Orientation = (Godot.PlaneMesh.OrientationEnum)Facing
		};

		return mesh;
	}

	public override int GetParameterHash()
	{
		return HashCode.Combine(Size, SubdivideWidth, SubdivideDepth, CenterOffset, Facing);
	}
}

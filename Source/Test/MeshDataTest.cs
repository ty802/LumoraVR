using System.Linq;
using Godot;

namespace Aquamarine.Source.Test;

public partial class MeshDataTest : Node
{
    public override void _Ready()
    {
        base._Ready();
        var meshScene = ResourceLoader.Load<PackedScene>("res://Assets/Test/socialvrtestmodel.glb");
        var instantiated = meshScene.Instantiate<Node3D>();

        var meshInstance = instantiated.FindChildren("*").OfType<MeshInstance3D>().FirstOrDefault();

        if (meshInstance?.Mesh is ArrayMesh mesh)
        {
            var blendShapeCount = mesh.GetBlendShapeCount();
            GD.Print($"Blend Shape Count: {mesh.GetBlendShapeCount()}");
            for (var i = 0; i < blendShapeCount; i++)
            {
                GD.Print($"{mesh.GetBlendShapeName(i)}");
            }

            var surfaceCount = mesh.GetSurfaceCount();
        }
    }
}

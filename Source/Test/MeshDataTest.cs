using System.Linq;
using Aquamarine.Source.Assets;
using Godot;

namespace Aquamarine.Source.Test;

public partial class MeshDataTest : Node3D
{
    public override void _Ready()
    {
        base._Ready();
        var meshScene = ResourceLoader.Load<PackedScene>("res://Assets/Test/socialvrtestmodel.glb");
        var instantiated = meshScene.Instantiate<Node3D>();

        var meshInstance = instantiated.FindChildren("*").OfType<MeshInstance3D>().FirstOrDefault();

        if (meshInstance?.Mesh is ArrayMesh mesh)
        {
            var meshFile = MeshFile.FromArrayMesh(mesh);
            var returnedMesh = meshFile.Instantiate();

            var instance = new MeshInstance3D();
            AddChild(instance);
            
            instance.Mesh = returnedMesh;
        }
    }
}

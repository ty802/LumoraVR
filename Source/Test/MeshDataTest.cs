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

        //this is our mesh, ripped from a standard godot import
        if (meshInstance?.Mesh is ArrayMesh mesh)
        {
            //this is our mesh, converted to a format better suited to serialization
            var meshFile = MeshFile.FromArrayMesh(mesh);
            
            GD.Print(meshFile.Valid());

            //this is our mesh converted to raw bytes
            var meshFileRaw = meshFile.Serialize();
            
            GD.Print(meshFileRaw.Length);

            //this is our mesh converted back to the format
            var meshFileDeserialized = MeshFile.Deserialize(meshFileRaw);
            
            //this is the formatted mesh converted back to a godot mesh
            var returnedMesh = meshFileDeserialized.Instantiate();
            //returnedMesh.BlendShapeMode = Mesh.BlendShapeMode.Relative;

            var instance = new MeshInstance3D();
            AddChild(instance);
            
            instance.Mesh = returnedMesh;
            instance.SetBlendShapeValue(1, 1);
            
            instance.SetSurfaceOverrideMaterial(0, new StandardMaterial3D()
            {
                AlbedoColor = Colors.Red
            });
            instance.SetSurfaceOverrideMaterial(1, new StandardMaterial3D()
            {
                AlbedoColor = Colors.Green
            });
            instance.SetSurfaceOverrideMaterial(2, new StandardMaterial3D()
            {
                AlbedoColor = Colors.Blue
            });
            instance.SetSurfaceOverrideMaterial(3, new StandardMaterial3D()
            {
                AlbedoColor = Colors.White
            });
            GD.Print(instance.GetBlendShapeCount());
        }
    }
}

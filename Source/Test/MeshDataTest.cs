using System.Linq;
using Aquamarine.Source.Assets;
using Aquamarine.Source.Scene;
using Aquamarine.Source.Scene.Assets;
using Aquamarine.Source.Scene.ChildObjects;
using Godot;
using Godot.Collections;

namespace Aquamarine.Source.Test;

public partial class MeshDataTest : Node3D
{
    public void ConversionTest()
    {
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
    public void TurnJohnAquamarineIntoAPrefab()
    {
        var getJohnsModel = ResourceLoader.Load<PackedScene>("res://Assets/Models/johnaquamarine.glb");
        
        var johnsModel = getJohnsModel.Instantiate<Node3D>();
        
        var meshInstance = johnsModel.FindChildren("*").OfType<MeshInstance3D>().FirstOrDefault();
        var skeleton = johnsModel.FindChildren("*").OfType<Skeleton3D>().FirstOrDefault();

        var meshFile = MeshFile.FromArrayMesh(meshInstance.Mesh as ArrayMesh);

        var meshFileAccess = FileAccess.Open("res://Assets/Models/johnaquamarine.meshfile", FileAccess.ModeFlags.Write);
        meshFileAccess.StoreBuffer(meshFile.Serialize());
        meshFileAccess.Close();
        
        var prefab = new Prefab();
        prefab.Type = RootObjectType.Avatar;

        var armature = new PrefabChild();
        prefab.Children[0] = armature;
        armature.Type = ChildObjectType.Armature;
        armature.Data = Armature.GenerateData(skeleton);

        var meshRenderer = new PrefabChild();
        prefab.Children[1] = meshRenderer;
        meshRenderer.Type = ChildObjectType.MeshRenderer;

        var meshProvider = new PrefabAsset();
        prefab.Assets[0] = meshProvider;
        meshProvider.Type = AssetProviderType.MeshFileProvider;
        
        meshProvider.Data = new Dictionary<string, Variant>
        {
            {"path", "res://Assets/Models/johnaquamarine.meshfile"},
        };

        var prefabFileAccess = FileAccess.Open("res://Assets/Prefabs/johnaquamarine.prefab", FileAccess.ModeFlags.Write);
        prefabFileAccess.StoreBuffer(prefab.Serialize().ToUtf8Buffer());
        prefabFileAccess.Close();
    }
    public override void _Ready()
    {
        base._Ready();
        TurnJohnAquamarineIntoAPrefab();
    }
}

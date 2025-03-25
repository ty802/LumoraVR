using System.Linq;
using Aquamarine.Source.Assets;
using Aquamarine.Source.Helpers;
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
            var (returnedMesh, _) = meshFileDeserialized.Instantiate();
            //returnedMesh.BlendShapeMode = Mesh.BlendShapeMode.Relative;

            var instance = new MeshInstance3D();
            AddChild(instance);

            instance.Mesh = returnedMesh;
            instance.SetBlendShapeValue(1, 1);

            instance.SetSurfaceOverrideMaterial(0, new StandardMaterial3D
            {
                AlbedoColor = Colors.Red,
            });
            instance.SetSurfaceOverrideMaterial(1, new StandardMaterial3D
            {
                AlbedoColor = Colors.Green,
            });
            instance.SetSurfaceOverrideMaterial(2, new StandardMaterial3D
            {
                AlbedoColor = Colors.Blue,
            });
            instance.SetSurfaceOverrideMaterial(3, new StandardMaterial3D
            {
                AlbedoColor = Colors.White,
            });
            //GD.Print(instance.GetBlendShapeCount());
        }
    }
    public void TurnJohnAquamarineHumanoidIntoAPrefab()
    {
        var getJohnsModel = ResourceLoader.Load<PackedScene>("res://Assets/Models/johnaquamarinehumanoid.glb");

        var johnsModel = getJohnsModel.Instantiate<Node3D>();

        var meshInstance = johnsModel.FindChildren("*", owned: false).OfType<MeshInstance3D>().FirstOrDefault();
        var skeleton = johnsModel.FindChildren("*", owned: false).OfType<Skeleton3D>().FirstOrDefault();

        if (meshInstance is null)
        {
            GD.Print("mesh is null");
            return;
        }
        if (meshInstance.Mesh is not ArrayMesh)
        {
            GD.Print(meshInstance.Mesh.GetType().ToString());
            return;
        }

        var meshFile = MeshFile.FromArrayMesh(meshInstance.Mesh as ArrayMesh, meshInstance.Skin);

        var meshFileAccess = FileAccess.Open("res://Assets/Models/johnaquamarinehumanoid.meshfile", FileAccess.ModeFlags.Write);
        meshFileAccess.StoreBuffer(meshFile.Serialize());
        meshFileAccess.Close();

        var prefab = new Prefab
        {
            Type = RootObjectType.Avatar,
        };

        var armature = new PrefabChild();
        prefab.Children[0] = armature;
        armature.Name = "Skeleton";
        armature.Type = ChildObjectType.Armature;
        armature.Data = Armature.GenerateData(skeleton);

        var meshRenderer = new PrefabChild();
        prefab.Children[1] = meshRenderer;
        meshRenderer.Name = "Mesh";
        meshRenderer.Type = ChildObjectType.MeshRenderer;
        meshRenderer.Data = new Dictionary<string, Variant>
        {
            {"mesh", 0},
            {"armature", 0},
            {"materials", new[]{ 1, 2, 3 }},
        };

        var animator = new PrefabChild();
        prefab.Children[2] = animator;
        animator.Name = "Animator";
        animator.Type = ChildObjectType.HumanoidAnimator;
        var offset = Mathf.Pi / 2;
        animator.Data = new Dictionary<string, Variant>
        {
            {"headBone", "Head"},
            {"hipBone", "Spine_0"},
            {"leftHandBone", "L_Hand"},
            {"rightHandBone", "R_Hand"},
            {"leftFootBone", "L_Foot"},
            {"rightFootBone", "R_Foot"},
            {"headOffset", new Transform3D(new Basis(Quaternion.FromEuler(new Vector3(0, Mathf.Pi, 0))), Vector3.Zero).ToFloatArray()},
            {"hipOffset", new Transform3D(new Basis(Quaternion.FromEuler(new Vector3(0, Mathf.Pi, 0))), Vector3.Zero).ToFloatArray()},
            {"leftHandOffset", new Transform3D(new Basis(Quaternion.FromEuler(new Vector3(Mathf.DegToRad(180), Mathf.DegToRad(90), Mathf.DegToRad(-90)))), Vector3.Zero).ToFloatArray()},
            {"rightHandOffset", new Transform3D(new Basis(Quaternion.FromEuler(new Vector3(Mathf.DegToRad(180), Mathf.DegToRad(-90), Mathf.DegToRad(90)))), Vector3.Zero).ToFloatArray()},
            {"armature", 0},
        };

        var meshProvider = new PrefabAsset();
        prefab.Assets[0] = meshProvider;
        meshProvider.Type = AssetProviderType.MeshFileProvider;
        meshProvider.Data = new Dictionary<string, Variant>
        {
            {"path", "builtin://Assets/Models/johnaquamarinehumanoid.meshfile"},
        };

        var material = new PrefabAsset();
        prefab.Assets[1] = material;
        material.Type = AssetProviderType.BasicMaterialProvider;
        material.Data = new Dictionary<string, Variant>
        {
            {"albedoColor", new Color("#e11545").ToFloatArray()},
            {"emissionColor", new Color("#e11545").ToFloatArray()},
            {"emissionStrength", 1f},
        };
        var material2 = new PrefabAsset();
        prefab.Assets[2] = material2;
        material2.Type = AssetProviderType.BasicMaterialProvider;
        material2.Data = new Dictionary<string, Variant>
        {
            {"albedoColor", Colors.White.ToFloatArray()},
        };
        var material3 = new PrefabAsset();
        prefab.Assets[3] = material3;
        material3.Type = AssetProviderType.BasicMaterialProvider;
        material3.Data = new Dictionary<string, Variant>
        {
            {"albedoColor", Colors.Black.ToFloatArray()},
        };

        var prefabFileAccess = FileAccess.Open("res://Assets/Prefabs/johnaquamarinehumanoid.prefab", FileAccess.ModeFlags.Write);
        prefabFileAccess.StoreBuffer(prefab.Serialize().ToUtf8Buffer());
        prefabFileAccess.Close();

        var prefabRead = FileAccess.Open("res://Assets/Prefabs/johnaquamarinehumanoid.prefab", FileAccess.ModeFlags.Read);
        var serialized = prefabRead.GetBuffer((long)prefabRead.GetLength()).GetStringFromUtf8();
        var pre = Prefab.Deserialize(serialized);

        var prefabInstantiated = pre.Instantiate();

        AddChild(prefabInstantiated.Self);

        GD.Print(serialized);
    }
    public void TurnJohnAquamarineIntoAPrefab()
    {
        var getJohnsModel = ResourceLoader.Load<PackedScene>("res://Assets/Models/johnaquamarine.glb");

        var johnsModel = getJohnsModel.Instantiate<Node3D>();

        var meshInstance = johnsModel.FindChildren("*", owned: false).OfType<MeshInstance3D>().FirstOrDefault();
        var skeleton = johnsModel.FindChildren("*", owned: false).OfType<Skeleton3D>().FirstOrDefault();

        if (meshInstance is null)
        {
            GD.Print("mesh is null");
            return;
        }
        if (meshInstance.Mesh is not ArrayMesh)
        {
            GD.Print(meshInstance.Mesh.GetType().ToString());
            return;
        }

        var meshFile = MeshFile.FromArrayMesh(meshInstance.Mesh as ArrayMesh, meshInstance.Skin);

        var meshFileAccess = FileAccess.Open("res://Assets/Models/johnaquamarine.meshfile", FileAccess.ModeFlags.Write);
        meshFileAccess.StoreBuffer(meshFile.Serialize());
        meshFileAccess.Close();

        var prefab = new Prefab
        {
            Type = RootObjectType.Avatar,
        };

        var armature = new PrefabChild();
        prefab.Children[0] = armature;
        armature.Name = "Skeleton";
        armature.Type = ChildObjectType.Armature;
        armature.Data = Armature.GenerateData(skeleton);

        var meshRenderer = new PrefabChild();
        prefab.Children[1] = meshRenderer;
        meshRenderer.Name = "Mesh";
        meshRenderer.Type = ChildObjectType.MeshRenderer;
        meshRenderer.Data = new Dictionary<string, Variant>
        {
            {"mesh", 0},
            {"armature", 0},
            {"materials", new[]{ 1, 2, 3 }},
        };

        var animator = new PrefabChild();
        prefab.Children[2] = animator;
        animator.Name = "Animator";
        animator.Type = ChildObjectType.HeadAndHandsAnimator;
        var offset = Mathf.Pi / 2;
        animator.Data = new Dictionary<string, Variant>
        {
            {"headBone", "Head"},
            {"leftHandBone", "L_Hand"},
            {"rightHandBone", "R_Hand"},
            {"headOffset", new Transform3D(new Basis(Quaternion.FromEuler(new Vector3(0, Mathf.Pi, 0))), Vector3.Zero).ToFloatArray()},
            {"leftHandOffset", new Transform3D(new Basis(Quaternion.FromEuler(new Vector3(Mathf.DegToRad(180), Mathf.DegToRad(90), Mathf.DegToRad(-90)))), Vector3.Zero).ToFloatArray()},
            {"rightHandOffset", new Transform3D(new Basis(Quaternion.FromEuler(new Vector3(Mathf.DegToRad(180), Mathf.DegToRad(-90), Mathf.DegToRad(90)))), Vector3.Zero).ToFloatArray()},
            {"armature", 0},
        };

        var meshProvider = new PrefabAsset();
        prefab.Assets[0] = meshProvider;
        meshProvider.Type = AssetProviderType.MeshFileProvider;
        meshProvider.Data = new Dictionary<string, Variant>
        {
            {"path", "builtin://Assets/Models/johnaquamarine.meshfile"},
        };

        var material = new PrefabAsset();
        prefab.Assets[1] = material;
        material.Type = AssetProviderType.BasicMaterialProvider;
        material.Data = new Dictionary<string, Variant>
        {
            {"albedoColor", new Color("#e11545").ToFloatArray()},
            {"emissionColor", new Color("#e11545").ToFloatArray()},
            {"emissionStrength", 1f},
        };
        var material2 = new PrefabAsset();
        prefab.Assets[2] = material2;
        material2.Type = AssetProviderType.BasicMaterialProvider;
        material2.Data = new Dictionary<string, Variant>
        {
            {"albedoColor", Colors.White.ToFloatArray()},
        };
        var material3 = new PrefabAsset();
        prefab.Assets[3] = material3;
        material3.Type = AssetProviderType.BasicMaterialProvider;
        material3.Data = new Dictionary<string, Variant>
        {
            {"albedoColor", Colors.Black.ToFloatArray()},
        };

        var prefabFileAccess = FileAccess.Open("res://Assets/Prefabs/johnaquamarine.prefab", FileAccess.ModeFlags.Write);
        prefabFileAccess.StoreBuffer(prefab.Serialize().ToUtf8Buffer());
        prefabFileAccess.Close();

        var prefabRead = FileAccess.Open("res://Assets/Prefabs/johnaquamarine.prefab", FileAccess.ModeFlags.Read);
        var serialized = prefabRead.GetBuffer((long)prefabRead.GetLength()).GetStringFromUtf8();
        var pre = Prefab.Deserialize(serialized);

        var prefabInstantiated = pre.Instantiate();

        AddChild(prefabInstantiated.Self);

        GD.Print(serialized);
    }
    public override void _Ready()
    {
        base._Ready();
        TurnJohnAquamarineHumanoidIntoAPrefab();
    }
}

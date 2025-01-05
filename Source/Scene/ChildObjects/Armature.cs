using System.Collections.Generic;
using System.Linq;
using Godot;
using Godot.Collections;

namespace Aquamarine.Source.Scene.ChildObjects;

public partial class Armature : Skeleton3D, IChildObject
{
    public Node Self => this;

    public Skin Skin;
    public void SetPlayerAuthority(int id) { }
    public void Initialize(Godot.Collections.Dictionary<string, Variant> data)
    {
        if (data.TryGetValue("transform", out var pos))
        {
            Transform = pos.AsTransform3D();
        }
        if (data.TryGetValue("bones", out var bones) && bones.VariantType == Variant.Type.Array)
        {
            ClearBones();
            
            var bo = new List<(string name, Transform3D rest, int parent)>();
            
            var boneList = bones.AsGodotArray();
            foreach (var bone in boneList.Select(i => i.AsGodotDictionary()))
            {
                bo.Add((bone["n"].AsString(), bone["r"].AsTransform3D(), bone["p"].AsInt32()));
            }
            
            foreach (var bone in bo) AddBone(bone.name);
            for (var index = 0; index < bo.Count; index++)
            {
                var bone = bo[index];
                SetBoneParent(index, bone.parent);
                SetBoneRest(index, bone.rest);
            }

            Skin = CreateSkinFromRestTransforms();
        }
    }

    //TEMP
    public static Godot.Collections.Dictionary<string, Variant> GenerateData(Skeleton3D skeleton)
    {
        var dict = new Godot.Collections.Dictionary<string, Variant>();

        if (!skeleton.Position.IsEqualApprox(Vector3.Zero) || !skeleton.Quaternion.IsEqualApprox(Quaternion.Identity) || !skeleton.Scale.IsEqualApprox(Vector3.Zero)) dict["transform"] = skeleton.Transform;

        var count = skeleton.GetBoneCount();

        var boneArray = new Array();
        
        for (var i = 0; i < count; i++)
        {
            var boneDict = new Dictionary();
            boneDict["n"] = skeleton.GetBoneName(i);
            boneDict["r"] = skeleton.GetBoneRest(i);
            boneDict["p"] = skeleton.GetBoneParent(i);
            boneArray.Add(boneDict);
        }

        dict["bones"] = boneArray;

        return dict;
    }
    
    public void AddChildObject(ISceneObject obj) => AddChild(obj.Self);
    public bool Dirty { get; }
    public IRootObject Root { get; set; }
    public ISceneObject Parent { get; set; }
}

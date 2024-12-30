using System.Collections.Generic;
using System.Linq;
using Godot;
using Godot.Collections;

namespace Aquamarine.Source.Scene.ChildObjects;

public partial class Armature : Skeleton3D, IChildObject
{
    public Node Self => this;
    public void SetPlayerAuthority(int id) { }
    public void Initialize(Godot.Collections.Dictionary<string, Variant> data)
    {
        if (data.TryGetValue("bones", out var bones) && bones.VariantType == Variant.Type.Array)
        {
            var bo = new List<(string name, Transform3D rest, int parent)>();
            
            var boneList = bones.AsGodotArray();
            foreach (var bone in boneList.Select(i => i.AsGodotDictionary()))
            {
                bo.Add((bone["n"].AsString(), bone["r"].AsTransform3D(), bone["p"].AsInt32()));
            }
            
            ClearBones();
            
            foreach (var bone in bo) AddBone(bone.name);
            for (var index = 0; index < bo.Count; index++)
            {
                var bone = bo[index];
                SetBoneParent(index, bone.parent);
                SetBoneRest(index, bone.rest);
            }
        }
    }
    public void AddChildObject(ISceneObject obj)
    {
        throw new System.NotImplementedException();
    }
    public bool Dirty { get; }
    public IRootObject Root { get; set; }
}

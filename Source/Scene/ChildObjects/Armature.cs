using System.Collections.Generic;
using Godot;

namespace Aquamarine.Source.Scene.ChildObjects;

public partial class Armature : Skeleton3D, IChildObject
{
    public Node Self => this;
    public void SetPlayerAuthority(int id) { }
    public void Initialize(Godot.Collections.Dictionary<string, Variant> data)
    {
        throw new System.NotImplementedException();
    }
    public void AddChildObject(ISceneObject obj)
    {
        throw new System.NotImplementedException();
    }
    public bool Dirty { get; }
    public IRootObject Root { get; set; }
}

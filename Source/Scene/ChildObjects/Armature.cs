using System.Collections.Generic;
using Godot;

namespace Aquamarine.Source.Scene.ChildObjects;

public partial class Armature : Skeleton3D, ISceneObject
{
    public Node Self => this;
    public void SetPlayerAuthority(int id) { }
    public void Initialize(Dictionary<string, Variant> data)
    {
        throw new System.NotImplementedException();
    }
    public void AddChild(Node node)
    {
        throw new System.NotImplementedException();
    }
}

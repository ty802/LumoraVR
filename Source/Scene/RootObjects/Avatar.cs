using System;
using System.Collections.Generic;
using System.IO;
using Aquamarine.Source.Helpers;
using Godot;

namespace Aquamarine.Source.Scene.ObjectTypes;

public enum AvatarAnimationType : byte
{
    Humanoid,
    HumanoidDigitigrade,
    HeadAndHands,
}

public partial class Avatar : Node3D, IRootObject
{
    public Node Self => this;
    public IDictionary<ushort,IChildObject> ChildObjects => _children;
    private readonly Dictionary<ushort,IChildObject> _children = new();
    public DirtyFlags64 DirtyFlags;

    public ICharacterController Parent;
    
    /*
    public AvatarAnimationType AvatarAnimationType;
    public Skeleton3D Skeleton;
    public string LeftHandBone;
    public string RightHandBone;
    public string LeftFootBone;
    public string RightFootBone;
    public string HipBone;
    public string HeadBone;
    */

    public void SetPlayerAuthority(int id)
    {
        throw new NotImplementedException();
    }
    public void Initialize(Dictionary<string, Variant> data)
    {
        throw new NotImplementedException();
    }
    public void AddChild(Node node)
    {
        throw new NotImplementedException();
    }
}

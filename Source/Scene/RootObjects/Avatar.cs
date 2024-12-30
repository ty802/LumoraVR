using System;
using System.Collections.Generic;
using Aquamarine.Source.Helpers;
using Aquamarine.Source.Scene;
using Godot;

namespace Aquamarine.Source.Scene.RootObjects;

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

    public void SetPlayerAuthority(int id)
    {
        throw new NotImplementedException();
    }
    public void Initialize(Godot.Collections.Dictionary<string, Variant> data)
    {
        throw new NotImplementedException();
    }
    public void AddChildObject(ISceneObject obj)
    {
        throw new NotImplementedException();
    }
}

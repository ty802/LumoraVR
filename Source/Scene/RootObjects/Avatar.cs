using System;
using System.Collections.Generic;
using Aquamarine.Source.Helpers;
using Aquamarine.Source.Scene.Assets;
using Godot;

namespace Aquamarine.Source.Scene.RootObjects;

public partial class Avatar : Node3D, IRootObject
{
    public Node Self => this;
    public IDictionary<ushort,IChildObject> ChildObjects { get; } = new Dictionary<ushort, IChildObject>();
    public IDictionary<ushort, IAssetProvider> AssetProviders { get; } = new Dictionary<ushort, IAssetProvider>();
    public DirtyFlags64 DirtyFlags;

    public ICharacterController Parent;

    public void SetPlayerAuthority(int id)
    {
        
    }
    public void Initialize(Godot.Collections.Dictionary<string, Variant> data)
    {
        
    }
    public void AddChildObject(ISceneObject obj) => AddChild(obj.Self);
}

using Aquamarine.Source.Scene.Assets;
using Godot;
using Godot.Collections;

namespace Aquamarine.Source.Scene.ChildObjects;

public partial class MeshRenderer : MeshInstance3D, IChildObject
{
    public IMeshProvider MeshProvider
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            if (value == null) Mesh = null;
            else
                value.Set(m =>
                {
                    Mesh = m;
                });
        }
    }
    public Armature Armature
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            Skeleton = value == null ? null : GetPathTo(field);
            Skin = value == null ? null : field.Skin;
        }
    }

    public Node Self => this;
    public void SetPlayerAuthority(int id)
    {
        
    }
    public void Initialize(Dictionary<string, Variant> data)
    {
        
    }
    public void AddChildObject(ISceneObject obj) => AddChild(obj.Self);
    public bool Dirty { get; }
    public IRootObject Root { get; set; }
    public ISceneObject Parent { get; set; }
}

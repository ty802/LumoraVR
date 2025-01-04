using Aquamarine.Source.Scene.Assets;
using Godot;
using Godot.Collections;

namespace Aquamarine.Source.Scene.ChildObjects;

public partial class MeshRenderer : MeshInstance3D, IChildObject
{
    public IMeshProvider MeshProvider
    {
        get => field;
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
    
    public Node Self => this;
    public void SetPlayerAuthority(int id)
    {
        
    }
    public void Initialize(Dictionary<string, Variant> data)
    {
        
    }
    public void AddChildObject(ISceneObject obj)
    {
        
    }
    public bool Dirty { get; }
    public IRootObject Root { get; set; }
    public ISceneObject Parent { get; set; }
}

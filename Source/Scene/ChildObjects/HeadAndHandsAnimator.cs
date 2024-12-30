using Aquamarine.Source.Scene.ObjectTypes;
using Godot;
using Godot.Collections;

namespace Aquamarine.Source.Scene.ChildObjects;

public partial class HeadAndHandsAnimator : Node, IChildObject
{
    public Node Self => this;

    public Armature Armature;
    public ICharacterController CharacterController;
    
    public string HeadBone;
    public string LeftHandBone;
    public string RightHandBone;
    
    public void SetPlayerAuthority(int id)
    {
        
    }
    public void Initialize(Dictionary<string, Variant> data)
    {
        HeadBone = data["headBone"].AsString();
        LeftHandBone = data["leftHandBone"].AsString();
        RightHandBone = data["rightHandBone"].AsString();

        if (Root is not ICharacterController charController) return;
        CharacterController = charController;

        var skeletonIndex = data["armature"].AsInt32();
        if (skeletonIndex >= 0 && Root.ChildObjects.TryGetValue((ushort)skeletonIndex, out var v) && v is Armature armature)
        {
            Armature = armature;
        }
    }
    public void AddChildObject(ISceneObject obj)
    {
        AddChild(obj.Self);
    }
    public bool Dirty => false;
    public IRootObject Root { get; set; }
}

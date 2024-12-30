using Aquamarine.Source.Input;
using Aquamarine.Source.Scene;
using Aquamarine.Source.Scene.RootObjects;
using Godot;
using Godot.Collections;

namespace Aquamarine.Source.Scene.ChildObjects;

public partial class HeadAndHandsAnimator : Node, IChildObject
{
    public Node Self => this;

    public Armature Armature { get; set { field = value; UpdateBoneIndices(); } }
    public ICharacterController CharacterController;
    
    public string HeadBone;
    public string LeftHandBone;
    public string RightHandBone;

    public int HeadBoneIndex;
    public int LeftHandBoneIndex;
    public int RightHandBoneIndex;
    
    
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
        if (skeletonIndex >= 0 && Root.ChildObjects.TryGetValue((ushort)skeletonIndex, out var v) && v is Armature armature) Armature = armature;
    }
    private void UpdateBoneIndices()
    {
        HeadBoneIndex = Armature.FindBone(HeadBone);
        LeftHandBoneIndex = Armature.FindBone(LeftHandBone);
        RightHandBoneIndex = Armature.FindBone(RightHandBone);
    }
    public override void _Process(double delta)
    {
        base._Process(delta);
        
        if (CharacterController is null || Armature is null) return;
        
        var headPose = CharacterController.GetLimbTransform(IInputProvider.InputLimb.Head);
        var leftHandPose = CharacterController.GetLimbTransform(IInputProvider.InputLimb.LeftHand);
        var rightHandPose = CharacterController.GetLimbTransform(IInputProvider.InputLimb.RightHand);
            
        Armature.SetBoneGlobalPose(HeadBoneIndex, new Transform3D(new Basis(headPose.rot), headPose.pos));
        Armature.SetBoneGlobalPose(LeftHandBoneIndex, new Transform3D(new Basis(leftHandPose.rot), leftHandPose.pos));
        Armature.SetBoneGlobalPose(RightHandBoneIndex, new Transform3D(new Basis(rightHandPose.rot), rightHandPose.pos));
    }
    public void AddChildObject(ISceneObject obj)
    {
        AddChild(obj.Self);
    }
    public bool Dirty => false;
    public IRootObject Root { get; set; }
    public ISceneObject Parent { get; set; }
}

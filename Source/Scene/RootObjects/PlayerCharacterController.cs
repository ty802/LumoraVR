using System;
using System.Collections.Generic;
using Aquamarine.Source.Input;
using Aquamarine.Source.Management;
using Godot;

namespace Aquamarine.Source.Scene.RootObjects;

public partial class PlayerCharacterController : CharacterBody3D, ICharacterController
{
    public Node Self => this;
    //public bool Dirty { get; set; }
    public IDictionary<ushort,IChildObject> ChildObjects => _children;
    private readonly Dictionary<ushort,IChildObject> _children = new();

    public static readonly PackedScene PackedScene = ResourceLoader.Load<PackedScene>("res://Scenes/Objects/RootObjects/PlayerCharacterController.tscn");

    public bool Debug = true;
    
    public Avatar Avatar;
    public string AvatarPrefab
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            if (Avatar is not null)
            {
                RemoveChild(Avatar);
                Avatar.QueueFree();
                Avatar = null;
            }
        }
    } = "DefaultAvatar";

    [Export] public MultiplayerSynchronizer ClientSync;
    [Export] public MultiplayerSynchronizer ServerSync;

    [Export] public Vector2 MovementInput;
    
    [Export] public Vector3 HeadPosition;
    [Export] public Quaternion HeadRotation = Quaternion.Identity;
    
    [Export] public Vector3 LeftHandPosition;
    [Export] public Quaternion LeftHandRotation = Quaternion.Identity;
    
    [Export] public Vector3 RightHandPosition;
    [Export] public Quaternion RightHandRotation = Quaternion.Identity;
    
    [Export] public Vector3 HipPosition;
    [Export] public Quaternion HipRotation = Quaternion.Identity;
    
    [Export] public Vector3 LeftFootPosition;
    [Export] public Quaternion LeftFootRotation = Quaternion.Identity;
    
    [Export] public Vector3 RightFootPosition;
    [Export] public Quaternion RightFootRotation = Quaternion.Identity;
    [Export] public Vector3 SyncVelocity
    {
        get => Velocity;
        set => Velocity = value;
    }
    [Export]
    public int Authority
    {
        get;
        set
        {
            field = value;
            ClientSync.SetMultiplayerAuthority(value);
        }
    }
    public override void _Process(double delta)
    {
        base._Process(delta);

        var deltaf = (float)delta;

        if (Avatar is null)
        {
            if (MultiplayerScene.Instance.Prefabs.TryGetValue(AvatarPrefab, out var prefab) && prefab.Type == RootObjectType.Avatar && prefab.Valid())
            {
                Avatar = prefab.Instantiate() as RootObjects.Avatar;
                AddChild(Avatar);
            }
        }

        var authority = ClientSync.IsMultiplayerAuthority();
        
        if (authority)
        {
            (HeadPosition, HeadRotation) = IInputProvider.LimbTransform(IInputProvider.InputLimb.Head);
            (LeftHandPosition, LeftHandRotation) = IInputProvider.LimbTransform(IInputProvider.InputLimb.LeftHand);
            (RightHandPosition, RightHandRotation) = IInputProvider.LimbTransform(IInputProvider.InputLimb.RightHand);
            MovementInput = IInputProvider.MovementInputAxis;
        }

        var yVel = IsOnFloor() ? 0 : Velocity.Y - (9.8f * deltaf);
        
        var headRotationFlat = ((HeadRotation * Vector3.Right) * new Vector3(1, 0, 1)).Normalized();
        var headRotation = new Vector2(headRotationFlat.X, headRotationFlat.Z).Angle();
        var movementRotated = MovementInput.Rotated(headRotation) * 5;
        
        Velocity = new Vector3(movementRotated.X, yVel, movementRotated.Y);

        MoveAndSlide();
        
        DebugDraw3D.DrawPosition(GlobalTransform * new Transform3D(new Basis(LeftHandRotation), LeftHandPosition));
        DebugDraw3D.DrawPosition(GlobalTransform * new Transform3D(new Basis(RightHandRotation), RightHandPosition));

        if (authority) IInputProvider.Move(GlobalTransform);
        else DebugDraw3D.DrawPosition(GlobalTransform * new Transform3D(new Basis(HeadRotation), HeadPosition));
    }
    public Vector3 GetLimbPosition(IInputProvider.InputLimb limb) =>
        limb switch
        {
            IInputProvider.InputLimb.Head => HeadPosition,
            IInputProvider.InputLimb.LeftHand => LeftHandPosition,
            IInputProvider.InputLimb.RightHand => RightHandPosition,
            IInputProvider.InputLimb.Hip => HipPosition,
            IInputProvider.InputLimb.LeftFoot => LeftFootPosition,
            IInputProvider.InputLimb.RightFoot => RightFootPosition,
            _ => Vector3.Zero,
        };
    public Quaternion GetLimbRotation(IInputProvider.InputLimb limb) =>
        limb switch
        {
            IInputProvider.InputLimb.Head => HeadRotation,
            IInputProvider.InputLimb.LeftHand => LeftHandRotation,
            IInputProvider.InputLimb.RightHand => RightHandRotation,
            IInputProvider.InputLimb.Hip => HipRotation,
            IInputProvider.InputLimb.LeftFoot => LeftFootRotation,
            IInputProvider.InputLimb.RightFoot => RightFootRotation,
            _ => Quaternion.Identity,
        };
    public void SetPlayerAuthority(int id) => Authority = id;
    public void Initialize(Godot.Collections.Dictionary<string, Variant> data)
    {
        throw new NotImplementedException();
    }
    public void AddChildObject(ISceneObject obj)
    {
        throw new NotImplementedException();
    }
}

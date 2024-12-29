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
    //public bool Dirty { get; set; }
    public IReadOnlyDictionary<ushort,IChildObject> ChildObjects => _children;
    private readonly Dictionary<ushort,IChildObject> _children = new();
    public DirtyFlags64 DirtyFlags;

    public ICharacterController Parent;
    
    public AvatarAnimationType AvatarAnimationType;
    public Skeleton3D Skeleton;
    public string LeftHandBone;
    public string RightHandBone;
    public string LeftFootBone;
    public string RightFootBone;
    public string HipBone;
    public string HeadBone;
    
    /*
    public void SendChanges() => this.InternalSendChanges();
    [Rpc(CallLocal = false, TransferChannel = SerializationHelpers.WorldUpdateChannel, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReceiveChanges(byte[] data) => this.InternalReceiveChanges(data);
    */

    public void SetPlayerAuthority(int id)
    {
        throw new NotImplementedException();
    }
    public void Serialize(BinaryWriter writer)
    {
        writer.Write(DirtyFlags);
        
        DirtyFlags.Clear();
    }
    public void Deserialize(BinaryReader reader)
    {
        var dirty = reader.ReadDirtyFlags64();
        
        DirtyFlags.Clear();
    }
}

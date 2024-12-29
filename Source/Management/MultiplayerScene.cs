using System.Linq;
using Aquamarine.Source.Helpers;
using Aquamarine.Source.Scene.ObjectTypes;
using Godot;

namespace Aquamarine.Source.Management;

public partial class MultiplayerScene : Node
{
    public static MultiplayerScene Instance;

    [Export] public MultiplayerSpawner Spawner;
    [Export] public Node3D PlayerRoot;

    public override void _Ready()
    {
        base._Ready();
        Instance = this;
    }
    [Rpc(CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = SerializationHelpers.WorldUpdateChannel)]
    private void InternalSpawnPlayer(int authority, Vector3 position)
    {
        var charController = PlayerCharacterController.PackedScene.Instantiate<PlayerCharacterController>();
        charController.SetPlayerAuthority(authority);
        charController.Name = authority.ToString();
        PlayerRoot.AddChild(charController);
        PlayerRoot.GlobalPosition = position;
    }
    [Rpc(CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = SerializationHelpers.WorldUpdateChannel)]
    private void InternalRemovePlayer(int authority)
    {
        var charController = PlayerRoot.GetChildren().FirstOrDefault(i => i is PlayerCharacterController cont && cont.Authority == authority);
        if (charController is null) return;
        PlayerRoot.RemoveChild(charController);
        charController.QueueFree();
    }
    public void SpawnPlayer(int authority, Vector3? position = null)
    {
        if (IsMultiplayerAuthority()) Rpc(MethodName.InternalSpawnPlayer, authority, position ?? Vector3.Zero);
    }
    public void RemovePlayer(int authority)
    {
        if (IsMultiplayerAuthority()) Rpc(MethodName.InternalRemovePlayer, authority);
    }
}

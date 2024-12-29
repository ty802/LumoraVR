using Aquamarine.Source.Input;
using Bones.Core;
using Godot;

namespace Aquamarine.Source.Management;

public partial class ClientManager : Node
{
    private XRInterface _xrInterface;
    private VRInput _vrInput;
    [Export] private Node3D _inputRoot;
    [Export] private MultiplayerScene _multiplayerScene;
    
    private const int Port = 7000;
    private const string DefaultServerIP = "127.0.0.1"; // IPv4 localhost
    private const int MaxConnections = 20;
    public override void _Ready()
    {
        _xrInterface = XRServer.FindInterface("OpenXR");
        if(IsInstanceValid(_xrInterface) && _xrInterface.IsInitialized())
        {
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
            GetViewport().UseXR = true;

            _vrInput = VRInput.PackedScene.Instantiate<VRInput>();
            _inputRoot.AddChild(_vrInput);
        }
        else
        {
            
        }
        
        this.CreateTimer(3, JoinServer);
    }
    public void JoinServer()
    {
        var peer = new ENetMultiplayerPeer();
        peer.CreateClient(DefaultServerIP, Port);
        
        /*_multiplayerScene.*/Multiplayer.MultiplayerPeer = peer;
    }
}

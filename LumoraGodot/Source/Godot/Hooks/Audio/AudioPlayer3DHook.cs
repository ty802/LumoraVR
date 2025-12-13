using Lumora.Core.Components.Audio;
using Godot;
namespace Aquamarine.Godot.Hooks;

public class AudioPlayer3DComponentHook : ComponentHook<AudioPlayer3D>
{
    private AudioStreamPlayer3D _player;
    public override void Initialize()
    {
        base.Initialize();
        _player = new AudioStreamPlayer3D();
        attachedNode.AddChild(_player);
    }
    public override void ApplyChanges()
    {
        _player.VolumeDb = Owner.VolumeDb;
        _player.Bus = System.Enum.GetName(typeof(AudioCategory),Owner.Category);
    }
}

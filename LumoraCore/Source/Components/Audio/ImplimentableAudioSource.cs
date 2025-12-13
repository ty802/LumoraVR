namespace Lumora.Core.Components.Audio;

public class ImplimentableAudioSource : ImplementableComponent
{
    public Sync<float>? VolumeDb { get; private set; }
    public Sync<AudioCategory>? Category { get; private set; }
    public override void OnAwake()
    {
        base.OnAwake();
        VolumeDb = new(this, 0);
        Category = new(this,AudioCategory.Effects);
    }
}

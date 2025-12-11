namespace Lumora.Core.Components.Audio;

public class ImplimentableAudioSource : ImplementableComponent
{
    public Sync<float>? _volumeDb;
    public override void OnAwake()
        {
            base.OnAwake();
            _volumeDb = new(this,0);
        }
}

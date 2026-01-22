using Godot;
using Lumora.Core.External.Audio.Capture;
using Lumora.Core.External.Audio.Capture.Godot;
#nullable enable
namespace Aquamarine.Source.Godot;
// dont know why i need the long form name but i dont want to fix it rn
public partial class CaptureManager : global::Lumora.Core.External.Audio.Capture.Godot.IGodotAudioCaptureManager
{
    public string[] GetCaptureDeviceNames() => AudioServer.GetInputDeviceList();

    public ILocalAudioStream? GetStreamForOrNull(string captureName)
    {
        AudioMixer mixer = AudioMixer.GetMixer();
        string busName = $"{captureName}-capture";
        AudioBus captureBus;
        if (mixer.TryGetAudioBusByName(busName, out captureBus))
            goto Success;

        if (mixer.CreateAudioBus(busName, out captureBus))
        {
            throw new System.NotImplementedException("yay");
            // Do initialization steps for new bus here
            goto Success;
        }

        return null;

    Success:
        // Create and return ILocalAudioStream using captureBus
        throw new System.NotImplementedException("yay");
    }
}

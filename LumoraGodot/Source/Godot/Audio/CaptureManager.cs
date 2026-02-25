using Godot;
using Lumora.Core;
using Lumora.Core.External.Audio.Capture;
using Lumora.Core.External.Audio.Capture.Godot;
#nullable enable
namespace Aquamarine.Source.Godot;
// dont know why i need the long form name but i dont want to fix it rn
public partial class CaptureManager : global::Lumora.Core.External.Audio.Capture.Godot.IGodotAudioCaptureManager
{
    public string[] GetCaptureDeviceNames() => AudioServer.GetInputDeviceList();

    public ResultEnum<ILocalAudioStream, GodotAudioStreamError> GetStreamFrom(string captureName)
    {
        AudioEffectCapture? cap = null;
        AudioMixer mixer = AudioMixer.GetMixer();
        string busName = $"{captureName}-capture";
        AudioBus captureBus;
        if (mixer.TryGetAudioBusByName(busName, out captureBus)) goto WithBus;
        if (mixer.CreateAudioBus(busName, out captureBus)) goto WithBus;
        return GodotAudioStreamError.FailedToGetBus;
    WithBus:
        int effectcount = AudioServer.GetBusEffectCount(captureBus.BusId);
        if (effectcount < 0) return GodotAudioStreamError.Unknown;
        if (effectcount > 1) return GodotAudioStreamError.InvalidBusConfiguration;
        if (effectcount < 1)
        {
            cap = new();
            AudioServer.AddBusEffect(captureBus.BusId, cap);
            goto WithCapture;
        }
        cap =  AudioServer.GetBusEffect(captureBus.BusId,0) as AudioEffectCapture;
        if(cap is null) return GodotAudioStreamError.InvalidBusConfiguration;
    WithCapture:
        // this is not finished
        throw new System.NotImplementedException();
    }
}

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
        var mixer = AudioMixer.GetMixer();
        var busName = $"{captureName}-capture";

        if (!mixer.TryGetAudioBusByName(busName, out var captureBus) &&
            !mixer.CreateAudioBus(busName, out captureBus))
            return GodotAudioStreamError.FailedToGetBus;

        var effectCount = AudioServer.GetBusEffectCount(captureBus.BusId);

        if (effectCount < 0) return GodotAudioStreamError.Unknown;
        if (effectCount > 1) return GodotAudioStreamError.InvalidBusConfiguration;

        AudioEffectCapture cap;
        if (effectCount == 0)
        {
            cap = new AudioEffectCapture();
            AudioServer.AddBusEffect(captureBus.BusId, cap);
        }
        else
        {
            cap = AudioServer.GetBusEffect(captureBus.BusId, 0) as AudioEffectCapture;
            if (cap is null) return GodotAudioStreamError.InvalidBusConfiguration;
        }
        return new LocalAudioSteam(cap);
    }
}

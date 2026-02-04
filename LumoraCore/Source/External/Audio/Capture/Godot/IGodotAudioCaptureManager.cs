namespace Lumora.Core.External.Audio.Capture.Godot;

public interface IGodotAudioCaptureManager
{
    public string[] GetCaptureDeviceNames();

    public ResultEnum<ILocalAudioStream,GodotAudioStreamError> GetStreamFrom(string captureName);
}

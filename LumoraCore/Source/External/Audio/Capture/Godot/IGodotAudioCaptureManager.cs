namespace Lumora.Core.External.Audio.Capture.Godot;

public interface IGodotAudioCaptureManager
{
    public string[] GetCaptureDeviceNames() ;

    public ILocalAudioStream? GetStreamForOrNull(string captureName);
}

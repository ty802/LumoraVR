
using Lumora.Core;
namespace Lumora.Core.External.Audio.Capture.Godot;
public enum GodotAudioStreamError : ushort {
    FailedToGetBus,
    InvalidBusConfiguration,
    FailToGetEffect,
    Unknown
}

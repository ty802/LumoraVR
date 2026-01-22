using System;
using Godot;
using System.Collections.Generic;
namespace Aquamarine.Source.Godot;

public partial class AudioMixer
{

    public string[] GetAvailableAudioEffects()
    {
        throw new NotImplementedException();
    }
    public static AudioMixer GetMixer() => _instance;
    private static readonly AudioMixer _instance = new AudioMixer();
    private readonly object _writeLock = new object();
    private readonly Dictionary<string, AudioBus> buses = new();
    private AudioMixer() { ForceSync(); }
    public void ForceSync()
    {
        foreach (var kv in buses)
        {
            kv.Value.Invalidate();
        }
        lock (_writeLock)
        {

            Lumora.Core.Logging.Logger.Log($"[Audio][ForceSync] There are {AudioServer.BusCount} buses in the engine");
            buses.Clear();
            for (int i = 0; i < AudioServer.BusCount; i++)
            {
                string name = AudioServer.GetBusName(i);
                Lumora.Core.Logging.Logger.Log($"[Audio][ForceSync] Adding bus {name}");
                var bus = new AudioBus(i);
                Lumora.Core.Logging.Logger.Log($"[Audio][ForceSync] the bus exists and its name is {bus.Name}");
                buses.Add(name, bus);
            }
        }
    }
}


using System;
using Godot;
using System.Collections.Generic;
using Lumora.Core.External.GenericAudioOutputMixer;
namespace Aquamarine.Source.Godot;

public partial class AudioBus : IAudioBus
{
    private int _busId;
    private bool _isValid = false;
    internal bool IsValid { get => _isValid; }
    internal void Invalidate()
    {
        _isValid = false;
    }
    public void SetVolumeDB(float volume)
    {
        if (!_isValid)
            throw new Exception("This Bus Refrence is no longer valid");
        AudioServer.SetBusVolumeDb(_busId, volume);
    }
    public void SetVolumeNormalized(float volume)
    {
        if (!_isValid)
            throw new Exception("This Bus Refrence is no longer valid");
        AudioServer.SetBusVolumeLinear(_busId, volume);
    }
    public float GetVolumeDB()
    {
        if (!_isValid)
            throw new Exception("This Bus Refrence is no longer valid");
        return AudioServer.GetBusVolumeDb(_busId);
    }
    public float GetVolumeNormalized()
    {
        if (!_isValid)
            throw new Exception("This Bus Refrence is no longer valid");
        return AudioServer.GetBusVolumeLinear(_busId);
    }
    public string Name
    {
        get
        {
            if (!_isValid)
                throw new Exception("This Bus Refrence is no longer valid");
            return AudioServer.GetBusName(_busId);
        }
        set
        {
            if (!_isValid)
                throw new Exception("This Bus Refrence is no longer valid");
            AudioServer.SetBusName(_busId, value);
        }
    }
    public List<IAudioEffect> Effects
    {
        get { throw new NotImplementedException(); }
    }
    public IAudioBus Target
    {
        get
        {
            if (!_isValid)
                throw new Exception("This Bus Refrence is no longer valid");
            return AudioMixer.GetMixer().GetAudioBusByName(AudioServer.GetBusSend(_busId));
        }
        set
        {
            if (!_isValid)
                throw new Exception("This Bus Refrence is no longer valid");
            AudioServer.SetBusSend(_busId, value.Name);
        }
    }
    internal int BusId
    {
        get => _busId;
    }
    internal AudioBus(int busid)
    {
        Lumora.Core.Logging.Logger.Log("creating bus");
        _isValid = true;
        _busId = busid;
    }
    public float Channels { get => 2; }
}
public partial class AudioMixer : IAudioMixer
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
    public IAudioBus GetAudioBusByName(string name)
    {
        if (buses.TryGetValue(name, out var b))
            return b;
        throw new Exception("I told you so");
    }
    public bool TryGetAudioBusByName(string name, out IAudioBus bus)
    {
#if DEBUG
        void logfname1(string log, [System.Runtime.CompilerServices.CallerFilePath] string fp = "", [System.Runtime.CompilerServices.CallerLineNumber] int ln = 1)
        {
            Lumora.Core.Logging.Logger.Log($"[{fp}:{ln}]: {log}");
        }

        if (name == "Master")
        {
            logfname1("This is master");
        }
        var st = new System.Diagnostics.StackTrace(true);
        var fr = st.GetFrame(1);
        Lumora.Core.Logging.Logger.Log($"[Audio] {fr.GetMethod().Name} is trying to get bus {name}");
#endif
        if (!buses.TryGetValue(name, out var rawbus))
        {
            bus = default;
            return false;
        }
        if (rawbus.IsValid)
        {
            bus = (IAudioBus)rawbus;
        }
        else
        {
            buses.Remove(name);
            bus = default;
            return false;
        }
        return true;
    }
#nullable enable
    public IAudioBus? GetAudioBusByNameOrNull(string name)
    {
        if (buses.TryGetValue(name, out var bus))
            return (IAudioBus)bus;
        return null;
    }
#nullable disable
    public bool CreateAudioBus(string name, out IAudioBus bus)
    {
        lock (_writeLock)
        {
            if (buses.ContainsKey(name))
            {
                if (buses.TryGetValue(name, out var ebus) && ebus.IsValid)
                {
                    bus = default; return false;
                }
                buses.Remove(name);
            }
            Lumora.Core.Logging.Logger.Log("trying");
            int newBusId = AudioServer.BusCount;
            AudioServer.AddBus(newBusId);
            AudioServer.SetBusName(newBusId, name);
            bus = new AudioBus(newBusId);
            return true;
        }
    }
    public IAudioBus[] GetAllBuses()
    {
        IAudioBus[] res = new IAudioBus[buses.Count];
        HashSet<int> indexs = new();
        foreach (var kv in buses)
        {
            if (!kv.Value.IsValid)
            {
                buses.Remove(kv.Key);
                continue;
            }
            int id = kv.Value.BusId;
            if (indexs.Add(id) && res.Length >= id)
            {
                res[id] = kv.Value;
            }
        }
        return res;
    }
}

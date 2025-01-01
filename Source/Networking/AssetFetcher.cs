using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;

namespace Aquamarine.Source.Networking;

[GlobalClass]
public partial class AssetFetcher : Node
{
    public delegate void OnLoaded(byte[] data);
    
    private static readonly Dictionary<string, (Task<byte[]> task, OnLoaded callback)> ActiveFetchTasks = new();
    
    public static void FetchAsset(string path, OnLoaded callback)
    {
        if (ActiveFetchTasks.TryGetValue(path, out var data))
        {
            data.callback += callback;
            return;
        }
        ActiveFetchTasks[path] = (StartFetchAsset(path, callback), _ => { });
    }
    public static async Task<byte[]> StartFetchAsset(string path, OnLoaded callback)
    {
        var uri = new Uri(path);
        var scheme = uri.Scheme;
        if (BuiltinAssetHelper.ValidPath(path)) return BuiltinAssetHelper.GetBuiltinAssetData(path);
        return null;
    }
    public static byte[] GetBuiltinAsset(string path) => BuiltinAssetHelper.GetBuiltinAssetData(path);
    public override void _Process(double delta)
    {
        base._Process(delta);
        
        foreach (var pair in ActiveFetchTasks)
        {
            var task = pair.Value.task;
            var callback = pair.Value.callback;
            if (task.IsCompleted) callback(task.Result);
        }
    }
}

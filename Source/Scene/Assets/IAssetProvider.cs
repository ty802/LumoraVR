using System;
using System.Collections.Generic;
using Aquamarine.Source.Assets;
using Aquamarine.Source.Networking;
using Godot;

namespace Aquamarine.Source.Scene.Assets;

public enum AssetProviderType
{
    //textures
    ImageTextureProvider,
    NoiseTextureProvider,
    
    //meshes
    MeshFileProvider,
    
    
}
public interface IAssetProvider
{
    public bool AssetReady { get; }
    public void Initialize(Godot.Collections.Dictionary<string, Variant> data);
}

public interface IAssetProvider<T> : IAssetProvider
{
    public void Set(Action<T> setAction);
}

public interface IFileAssetProvider<T> : IAssetProvider<T>
{
    public string Path { get; set; }
    public T Asset { get; set; }

    public T ParseAsset(byte[] data);
}

public static class SimpleAssetCache
{
    public static void ClearAllCaches()
    {
        SimpleAssetCache<Mesh>.Cache.Clear();
        SimpleAssetCache<Texture2D>.Cache.Clear();
    }
}
public static class SimpleAssetCache<T>
{
    public static readonly Dictionary<string,T> Cache = new();

    public static void DoInitialize(IFileAssetProvider<T> provider, Godot.Collections.Dictionary<string, Variant> data)
    {
        if (!data.TryGetValue("path", out var p)) return;

        var path = p.AsString().Trim();
        provider.Path = path;

        //if we have already cached the texture, pull it from the cache, otherwise start fetching it
        if (Cache.TryGetValue(path, out var result)) provider.Asset = result;
        else provider.Set(_ => { });
    }
    public static void DoSet(IFileAssetProvider<T> provider, Action<T> setAction)
    {
        if (provider.AssetReady) setAction(provider.Asset);
        else
        {
            var path = provider.Path;
            AssetFetcher.FetchAsset(path, bytes =>
            {
                if (Cache.TryGetValue(path, out var result))
                {
                    provider.Asset = result;
                    setAction(result);
                    return;
                }

                var parsed = provider.ParseAsset(bytes);
                
                provider.Asset = parsed;
                Cache[path] = parsed;
                setAction(parsed);
            });
        }
    }
}
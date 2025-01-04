using System;
using Aquamarine.Source.Assets;
using Godot;
using Godot.Collections;

namespace Aquamarine.Source.Scene.Assets;

public class MeshFileProvider : IMeshProvider, IFileAssetProvider<Mesh>
{
    public void Set(Action<Mesh> setAction) => SimpleAssetCache<Mesh>.DoSet(this, setAction);
    public bool AssetReady => Asset is not null;
    public void Initialize(Dictionary<string, Variant> data) => SimpleAssetCache<Mesh>.DoInitialize(this, data);
    public string Path { get; set; }
    public Mesh Asset { get; set; }
    public Mesh ParseAsset(byte[] data) => AssetParser.ParseMesh(data);
}

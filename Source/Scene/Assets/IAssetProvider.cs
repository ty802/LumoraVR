using Godot;

namespace Aquamarine.Source.Scene.Assets;

public enum AssetProviderType
{
    //textures
    ImageTextureProvider,
    NoiseTextureProvider,
}
public interface IAssetProvider
{
    public void Initialize(Godot.Collections.Dictionary<string, Variant> data);
}

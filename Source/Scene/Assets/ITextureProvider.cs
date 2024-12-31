using Godot;

namespace Aquamarine.Source.Scene.Assets;

public interface ITextureProvider : IAssetProvider
{
    public Texture2D Texture { get; }
}

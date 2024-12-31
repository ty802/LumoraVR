using Godot;
using Godot.Collections;

namespace Aquamarine.Source.Scene.Assets;

public class NoiseTextureProvider : ITextureProvider
{
    public Texture2D Texture => _texture;
    private NoiseTexture2D _texture = new();
    public void Initialize(Dictionary<string, Variant> data)
    {
        throw new System.NotImplementedException();
    }
}

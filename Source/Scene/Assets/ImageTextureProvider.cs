using Godot;
using Godot.Collections;

namespace Aquamarine.Source.Scene.Assets;

public class ImageTextureProvider : ITextureProvider
{
    public string Path;
    public void Initialize(Dictionary<string, Variant> data)
    {
        throw new System.NotImplementedException();
    }
    public Texture2D Texture { get; }
}

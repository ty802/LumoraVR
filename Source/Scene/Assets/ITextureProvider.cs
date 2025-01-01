using System;
using Godot;

namespace Aquamarine.Source.Scene.Assets;

public interface ITextureProvider : IAssetProvider
{
    public void Set(Action<Texture2D> setAction);
}
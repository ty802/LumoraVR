using System;
using Godot;

namespace Aquamarine.Source.Scene.Assets;

public interface ITextureProvider : IAssetProvider<Texture2D>
{
    //public void Set(Action<Texture2D> setAction);
}
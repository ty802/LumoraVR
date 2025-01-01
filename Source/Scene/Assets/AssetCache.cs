using System.Collections.Generic;

namespace Aquamarine.Source.Scene.Assets;

public class AssetCache<T>
{
    public readonly Dictionary<string,T> Cache = new();
}

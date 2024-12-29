using System.IO;
using Aquamarine.Source.Scene.ObjectTypes;

namespace Aquamarine.Source.Scene;

public interface IChildObject : ISceneObject
{
    public bool Dirty { get; }
    public IRootObject Root { get; set; }

    public void PopulateRoot()
    {
        if (Root is not null) return;
        var parent = Self.GetParent();
        while (parent is not null)
        {
            if (parent is IRootObject rootObject)
            {
                Root = rootObject;
                return;
            }
            parent = parent.GetParent();
        }
    }
}

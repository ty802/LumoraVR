using System;
using System.Collections.Generic;
using System.IO;
using Godot;

namespace Aquamarine.Source.Scene;

public interface ISceneObject
{
    public Node Self { get; }
    public bool EnsureValidReference(ref GodotObject obj)
    {
        if (GodotObject.IsInstanceValid(obj)) return true;
        obj = null;
        return false;
    }
    public void SetPlayerAuthority(int id);
    public void Initialize(Dictionary<string, Variant> data);
    public void AddChild(Node node);
}

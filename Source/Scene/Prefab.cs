using System;
using Aquamarine.Source.Scene.ObjectTypes;
using Bones.Core;
using Godot;
using Godot.Collections;

namespace Aquamarine.Source.Scene;

public class Prefab
{
    public int Version;
    public RootObjectType Type;
    public Dictionary<string, Variant> Data = new();
    public System.Collections.Generic.Dictionary<ushort, PrefabChild> Children = new();

    public static Prefab Deserialize(string json)
    {
        var prefab = new Prefab();
        var collection = Json.ParseString(json);
        if (collection.VariantType == Variant.Type.Dictionary)
        {
            var dict = collection.AsGodotDictionary();
            if (dict.TryGetValue("version", out var v)) prefab.Version = v.AsInt32();
            if (dict.TryGetValue("type", out var t)) prefab.Type = Enum.Parse<RootObjectType>(t.AsString(), true);
            if (dict.TryGetValue("data", out var d)) prefab.Data = d.AsGodotDictionary<string, Variant>();
            if (dict.TryGetValue("children", out var c))
            {
                var childrenDict = c.AsGodotDictionary<ushort, Dictionary>();
                foreach (var pair in childrenDict) prefab.Children[pair.Key] = PrefabChild.Deserialize(pair.Value);
            }
        }
        return prefab;
    }

    public IRootObject Instantiate()
    {
        if (!Valid()) return null;

        var obj = Type switch
        {
            RootObjectType.Avatar => new Avatar(),
            _ => throw new ArgumentOutOfRangeException(),
        };

        var children = new System.Collections.Generic.Dictionary<PrefabChild, IChildObject>();
        foreach (var (index, prefabChild) in Children)
        {
            var child = prefabChild.Instantiate();
            obj.ChildObjects.Add(index, child);
            children.Add(prefabChild, child);
        }
        foreach (var (prefabChild, childObj) in children)
        {
            
        }

        return obj;
    }

    public bool Valid()
    {
        //TODO: more validity checks, like the content of data
        if (!Type.CanInstantiate()) return false;
        return true;
    }
    
    public string Serialize()
    {
        var dict = new Dictionary();
        dict["version"] = Version;
        dict["type"] = EnumHelpers<RootObjectType>.ToStringLowerCached(Type);
        dict["data"] = Data;

        var childrenDict = new Dictionary<ushort, Dictionary>();
        foreach (var pair in Children) childrenDict[pair.Key] = pair.Value.Serialize();
        dict["children"] = childrenDict;
        
        var str = Json.Stringify(dict);
        return str;
    }
}

public class PrefabChild
{
    public int Parent; //-1 if parented to the root object, otherwise the ushort index of the parent
    public ChildObjectType Type;
    public Dictionary<string, Variant> Data = new();
    
    public static PrefabChild Deserialize(Dictionary dict)
    {
        var prefab = new PrefabChild();
        
        if (dict.TryGetValue("p", out var v)) prefab.Parent = v.AsInt32();
        if (dict.TryGetValue("t", out var t)) prefab.Type = Enum.Parse<ChildObjectType>(t.AsString(), true);
        if (dict.TryGetValue("d", out var d)) prefab.Data = d.AsGodotDictionary<string, Variant>();
        
        return prefab;
    }
    public IChildObject Instantiate()
    {
        return null;
    }
    public Dictionary Serialize()
    {
        var dict = new Dictionary();
        dict["p"] = Parent;
        dict["t"] = EnumHelpers<ChildObjectType>.ToStringLowerCached(Type);
        dict["d"] = Data;
        return dict;
    }
}

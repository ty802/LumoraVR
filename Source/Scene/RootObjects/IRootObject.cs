using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Aquamarine.Source.Helpers;
using Aquamarine.Source;
using Godot;

namespace Aquamarine.Source.Scene.ObjectTypes;

public interface IRootObject : ISceneObject
{
    //public bool Dirty { get; set; }
    //public IReadOnlyDictionary<ushort,IChildObject> ChildObjects { get; }

    //public void SendChanges();
    //public void ReceiveChanges(byte[] data);

    //public void InitializeChildren();
}
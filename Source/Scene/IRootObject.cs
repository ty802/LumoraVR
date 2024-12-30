using System.Collections.Generic;

namespace Aquamarine.Source.Scene;

public interface IRootObject : ISceneObject
{
    //public bool Dirty { get; set; }
    public IDictionary<ushort,IChildObject> ChildObjects { get; }

    //public void SendChanges();
    //public void ReceiveChanges(byte[] data);

    //public void InitializeChildren();
}
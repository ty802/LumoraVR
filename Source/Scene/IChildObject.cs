namespace Aquamarine.Source.Scene;

public enum ChildObjectType
{
    None,
    //Node,
    MeshRenderer,
    Armature,
    HeadAndHandsAnimator,
    SpineAnimator,
}

public interface IChildObject : ISceneObject
{
    public bool Dirty { get; }
    public IRootObject Root { get; set; }
    public ISceneObject Parent { get; set; }

    /*
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
    */
}

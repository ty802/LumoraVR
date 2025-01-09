namespace Aquamarine.Source.Scene.Editors;

public interface IPrefabEditor
{
    public delegate void OnHierarchyChanged();
    public Prefab EditingPrefab { get; set; }
    public OnHierarchyChanged HierarchyChanged { get; set; }
}

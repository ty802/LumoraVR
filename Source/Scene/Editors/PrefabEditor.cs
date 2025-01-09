using Godot;

namespace Aquamarine.Source.Scene.Editors;

public partial class PrefabEditor : PanelContainer
{
    [Export] public RootObjectType Type { get; set; }
    [ExportGroup("Internal")]
    [Export] public AssetEditor AssetEditor;
    [Export] public HierarchyEditor HierarchyEditor;
    

    public delegate void OnHierarchyChanged();
    
    public Prefab EditingPrefab { get; set; }
    public OnHierarchyChanged HierarchyChanged = () => { };

    public override void _Ready()
    {
        base._Ready();
        
        //TEMP
        var prefabRead = FileAccess.Open("res://Assets/Prefabs/johnaquamarine.prefab", FileAccess.ModeFlags.Read);
        var serialized = prefabRead.GetBuffer((long)prefabRead.GetLength()).GetStringFromUtf8();
        EditingPrefab = Prefab.Deserialize(serialized);

        CallDeferred(MethodName.DeferInit);
    }

    private void DeferInit()
    {
        HierarchyChanged();
    }
}

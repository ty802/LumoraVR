using Godot;

namespace Aquamarine.Source.Scene.Editors;

public partial class AvatarEditor : PanelContainer, IPrefabEditor
{
    [Export] public AssetEditor AssetEditor;
    [Export] public HierarchyEditor HierarchyEditor;
    public Prefab EditingPrefab { get; set; }
    public IPrefabEditor.OnHierarchyChanged HierarchyChanged { get; set; } = () => { };

    public override void _Ready()
    {
        base._Ready();
        AssetEditor.PrefabEditor = this;
        HierarchyEditor.PrefabEditor = this;
        
        //TEMP
        var prefabRead = FileAccess.Open("res://Assets/Prefabs/johnaquamarine.prefab", FileAccess.ModeFlags.Read);
        var serialized = prefabRead.GetBuffer((long)prefabRead.GetLength()).GetStringFromUtf8();
        EditingPrefab = Prefab.Deserialize(serialized);
    }
}

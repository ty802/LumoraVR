using Godot;

namespace Aquamarine.Source.Scene.Editors;

public partial class AssetEditor : PanelContainer
{
    [Export] public PrefabEditor PrefabEditor;
    [Export] public OptionButton AssetTypeOptions;
    [Export] public Button AssetAddButton;

    public override void _Ready()
    {
        base._Ready();
        
    }
}

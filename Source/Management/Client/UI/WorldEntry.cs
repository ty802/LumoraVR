using Aquamarine.Source.Management.Data;
using Godot;

namespace Aquamarine.Source.Management.Client.UI;

public partial class WorldEntry : PanelContainer
{
    private WorldsTab _tab;
    private string _worldId;
    private bool _isLocal;
    private LocalWorldInfo _localInfo;
    private RichTextLabel _worldNameLabel;
    private TextureRect _previewTexture;

    public bool IsLocal => _isLocal;

    public override void _Ready()
    {
        base._Ready();
        _worldNameLabel = GetNodeOrNull<RichTextLabel>("%WorldNameLabel")
            ?? GetNode<RichTextLabel>("MarginContainer/VBoxContainer/RichTextLabelAutoSizeNode/WorldNameLabel");
        _previewTexture = GetNode<TextureRect>("MarginContainer/VBoxContainer/Control/TextureRect");
    }

    public void AssignRemoteWorld(WorldsTab tab, string worldId, string worldName)
    {
        if (_tab != null)
        {
            _tab.SessionListUpdated -= Tab_SessionListUpdated;
        }

        _tab = tab;
        _worldId = worldId;
        _isLocal = false;
        _localInfo = null;

        _tab.SessionListUpdated += Tab_SessionListUpdated;
        UpdateTitle(worldName);
    }

    public void AssignLocalWorld(WorldsTab tab, LocalWorldInfo info)
    {
        if (_tab != null && !_isLocal)
        {
            _tab.SessionListUpdated -= Tab_SessionListUpdated;
        }

        _tab = tab;
        _worldId = info.WorldId;
        _isLocal = true;
        _localInfo = info;

        UpdateTitle(info.DisplayName);
        UpdatePreview(info.Preview);
    }

    private void Tab_SessionListUpdated()
    {
        if (_isLocal)
        {
            if (_localInfo != null)
            {
                UpdateTitle(_localInfo.DisplayName);
                UpdatePreview(_localInfo.Preview);
            }
            return;
        }

        if (_tab == null)
        {
            QueueFree();
            return;
        }

        if (!_tab.Sessions.TryGetValue(_worldId, out var worldEntry))
        {
            _tab.SessionListUpdated -= Tab_SessionListUpdated;
            QueueFree();
            return;
        }

        UpdateTitle(worldEntry.WorldName);
    }

    public void OnDetailsButtonPressed()
    {
        _tab?.LoadWorldInfo(_worldId);
    }

    public override void _ExitTree()
    {
        base._ExitTree();
        if (_tab != null)
        {
            _tab.SessionListUpdated -= Tab_SessionListUpdated;
        }
    }

    private void UpdateTitle(string name)
    {
        if (_worldNameLabel != null)
        {
            _worldNameLabel.Text = $"[center]{name}[/center]";
        }
    }

    private void UpdatePreview(Texture2D preview)
    {
        if (_previewTexture != null && preview != null)
        {
            _previewTexture.Texture = preview;
        }
    }
}

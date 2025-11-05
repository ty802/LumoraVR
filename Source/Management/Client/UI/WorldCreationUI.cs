using System;
using System.Linq;
using Godot;
using Aquamarine.Source.Core;
using Aquamarine.Source.Core.WorldTemplates;
using AquaLogger = Aquamarine.Source.Logging.Logger;

namespace Aquamarine.Source.Management.Client.UI;

/// <summary>
/// UI for creating and hosting new worlds with template selection.
/// 
/// </summary>
public partial class WorldCreationUI : MarginContainer
{
    private LineEdit _worldNameEdit;
    private GridContainer _templateGrid;
    private Button _createButton;
    private Button _cancelButton;

    private string _selectedTemplate = "Grid"; // Default template

    [Signal]
    public delegate void WorldCreatedEventHandler(string worldName, string templateName);

    [Signal]
    public delegate void CancelledEventHandler();

    public override void _Ready()
    {
        base._Ready();

        _worldNameEdit = GetNode<LineEdit>("%LineEdit");
        _templateGrid = GetNode<GridContainer>("%TemplateGrid");
        _createButton = GetNode<Button>("%CreateButton");
        _cancelButton = GetNode<Button>("%CancelButton");

        // Connect signals
        _createButton.Pressed += OnCreatePressed;
        _cancelButton.Pressed += OnCancelPressed;

        // Populate templates
        PopulateTemplates();

        // Set default world name
        _worldNameEdit.Text = $"My World {DateTime.Now:HHmm}";
    }

    private void PopulateTemplates()
    {
        var templates = TemplateManager.GetAllTemplates().ToList();

        foreach (var template in templates)
        {
            var templateButton = CreateTemplateButton(template);
            _templateGrid.AddChild(templateButton);
        }

        AquaLogger.Log($"Populated {templates.Count} world templates");
    }

    private Control CreateTemplateButton(WorldTemplate template)
    {
        // Create a container for the template
        var container = new VBoxContainer();
        container.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        container.AddThemeConstantOverride("separation", 8);

        // Create preview image using template preview texture
        var previewTexture = new TextureRect
        {
            CustomMinimumSize = new Vector2(250, 150),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            ExpandMode = TextureRect.ExpandModeEnum.FitWidth,
            Texture = template.GetPreviewTexture()
        };
        container.AddChild(previewTexture);

        // Create template button
        var button = new Button();
        button.Text = $"{template.Name}\n{template.Category}";
        button.CustomMinimumSize = new Vector2(0, 80);
        button.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        button.Pressed += () => OnTemplateSelected(template.Name);
        container.AddChild(button);

        // Create description label
        var descLabel = new Label();
        descLabel.Text = template.Description;
        descLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        descLabel.HorizontalAlignment = HorizontalAlignment.Center;
        descLabel.AddThemeFontSizeOverride("font_size", 18);
        container.AddChild(descLabel);

        return container;
    }

    private void OnTemplateSelected(string templateName)
    {
        _selectedTemplate = templateName;
        AquaLogger.Log($"Selected template: {templateName}");

        // Visual feedback - highlight selected template
        // TODO: Add visual highlight to selected button
    }

    private void OnCreatePressed()
    {
        string worldName = _worldNameEdit.Text.Trim();

        if (string.IsNullOrEmpty(worldName))
        {
            AquaLogger.Warn("World name cannot be empty");
            _worldNameEdit.PlaceholderText = "Please enter a world name!";
            return;
        }

        AquaLogger.Log($"Creating world '{worldName}' with template '{_selectedTemplate}'");
        EmitSignal(SignalName.WorldCreated, worldName, _selectedTemplate);
    }

    private void OnCancelPressed()
    {
        AquaLogger.Log("World creation cancelled");
        EmitSignal(SignalName.Cancelled);
    }
}

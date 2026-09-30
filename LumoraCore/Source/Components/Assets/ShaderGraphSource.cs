// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Linq;
using Helio.UI;
using Lumora.Core.Assets;
using Lumora.Core.Components.Scripting;
using Lumora.Core.Scripting;
using Lumora.Script;
using Lumora.Shading;

namespace Lumora.Core.Components.Assets;

// The shader a shader graph makes, for a CustomShaderMaterial to wear. Every machine compiles the graph it
// has: while the graph is open, from the graph's own check a moment after each edit, so the material
// follows the graph as it is built; while it is closed, straight from its nodes when the material first
// needs a shader here. No shader text ever travels. A graph with a problem keeps the last shader that
// compiled.
//
// Material is the material whose properties hold what the graph takes from outside, its textures: a
// texture slot on a card and a texture row on the material panel are the same property. -xlinka
[ComponentCategory("Assets/Materials")]
public sealed class ShaderGraphSource : DynamicAssetProvider<ShaderSourceAsset>, ICustomInspectorUI, IInspectorActionHandler
{
    public readonly SyncRef<ScriptGraphSpace> Graph = new();

    public readonly SyncRef<CustomShaderMaterial> Material = new();

    private string _source = string.Empty;
    private ShaderProgram? _program;
    private ScriptGraphSpace? _compiledFrom;
    private bool _compileQueued;
    private bool _attempted;

    // The last compile, whether or not it worked.
    public ShaderProgram? LastProgram => _program;

    // The shader the material wears: the last one that compiled.
    public string Source => _source;

    public bool IsGraphOpen => Graph.Target is { IsDestroyed: false, Slot.IsActive: true };

    protected override void OnAssetCreated(ShaderSourceAsset asset)
    {
    }

    // Also what runs when Graph is pointed somewhere else, which compiles the new graph. That compile waits
    // for the next frame: an orb being copied, loaded or received gets its graph's wires only once all of it
    // is there, and a shader made from half a graph would cost the material every property only the whole
    // graph declares.
    protected override void UpdateAsset(ShaderSourceAsset asset)
    {
        if ((!_attempted || !ReferenceEquals(_compiledFrom, Graph.Target)) && !_compileQueued)
        {
            _compileQueued = true;
            if (World is { } world)
                world.RunSynchronously(CompileQueued);
            else
                _compileQueued = false;
        }
        asset.SetSource(_source);
    }

    private void CompileQueued()
    {
        _compileQueued = false;
        if (IsDestroyed)
            return;
        Compile();
        MarkChangeDirty();
    }

    protected override void OnAssetCleared()
    {
    }

    // What the open graph's check found, on every machine it is open on.
    internal void TakeCheck(ScriptGraphSpace space, ShaderProgram program)
    {
        if (!ReferenceEquals(space, Graph.Target))
            return;
        _compiledFrom = space;
        if (Take(program))
            MarkChangeDirty();
    }

    // Straight from the graph's nodes, open or not.
    public void CompileNow()
    {
        Compile();
        MarkChangeDirty();
    }

    private void Compile()
    {
        var space = Graph.Target;
        _compiledFrom = space;
        _attempted = true;
        if (space == null || space.IsDestroyed)
        {
            _program = null;
            return;
        }
        var build = ScriptGraphPacking.Build(space, withPositions: false);
        if (ShaderGraphLanguage.Instance.Check(build.Graph).Shader is { } program)
            Take(program);
    }

    private bool Take(ShaderProgram program)
    {
        _program = program;
        if (!program.Succeeded || program.Source == _source)
            return false;
        _source = program.Source;
        return true;
    }

    // The texture a uniform of the graph's shader has on the material.
    public IAssetProvider<TextureAsset>? TextureFor(string uniform)
    {
        if (Material.Target is not { IsDestroyed: false } material)
            return null;
        foreach (var param in material.Parameters)
        {
            if (param.Name.Value == uniform)
                return param.Texture.Target;
        }
        return null;
    }

    // Puts a texture in a uniform's property on the material, what a card's texture slot does with a texture
    // dropped on it. A property the host has not made yet is made here; the host keeps its value by name
    // when it rebuilds the list for the new shader.
    public ShaderUniformParam? AssignTexture(string uniform, IAssetProvider<TextureAsset>? texture)
    {
        if (Material.Target is not { IsDestroyed: false } material || string.IsNullOrEmpty(uniform))
            return null;
        var param = material.Parameters.FirstOrDefault(p => p.Name.Value == uniform);
        if (param == null)
        {
            param = material.Parameters.Add();
            param.Name.Value = uniform;
            param.Type.Value = ShaderUniformType.Texture2D;
        }
        param.Texture.Target = texture!;
        return param;
    }

    public void HandleInspectorAction(string argument)
    {
        if (argument == "togglegraph")
            ShaderGraphMaterials.SetOpen(this, !IsGraphOpen);
    }

    public void BuildInspectorBody(UIBuilder ui)
    {
        var space = Graph.Target;
        bool hasGraph = space != null && !space.IsDestroyed;
        int nodes = hasGraph ? space!.Nodes.Count() : 0;
        StatRow(ui, "Graph", !hasGraph ? "none" : IsGraphOpen ? $"open, {nodes} nodes" : $"closed, {nodes} nodes");
        if (_program == null)
        {
            StatRow(ui, "Shader", "not compiled here yet");
        }
        else
        {
            var errors = _program.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            StatRow(ui, "Shader", errors.Count == 0 ? "compiled" : $"{errors.Count} problem(s), wearing the last good one");
            if (errors.Count > 0)
                StatRow(ui, string.Empty, errors[0].Message);
            StatRow(ui, "Properties", _program.Uniforms.Count.ToString());
            StatRow(ui, "Texture reads", _program.TextureSamples.ToString());
        }
        if (hasGraph)
        {
            InspectorUI.FixedRow(ui.Root, "Graph", 30f, out var rowUi, ui.Root);
            InspectorUI.RelayButton(rowUi, this, "togglegraph", IsGraphOpen ? "Close graph" : "Open graph", 0f);
        }
    }

    private static void StatRow(UIBuilder ui, string label, string value)
    {
        // Theme from the hosting panel's UI tree: this component's own slot has no UITheme above it.
        InspectorUI.FixedRow(ui.Root, label, 24f, out var rowUi, ui.Root);
        rowUi.PushStyle();
        rowUi.MinWidth(150f);
        rowUi.PreferredWidth(190f);
        rowUi.FlexibleWidth(0f);
        var labelText = rowUi.Text(label, InspectorUI.FontSize - 1f, InspectorUI.MutedColor);
        InspectorUI.FillParent(labelText.RectTransform!);
        labelText.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        labelText.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        rowUi.PopStyle();
        rowUi.PushStyle();
        rowUi.FlexibleWidth(1f);
        var valueText = rowUi.Text(value, InspectorUI.FontSize - 1f, InspectorUI.TextColor);
        InspectorUI.FillParent(valueText.RectTransform!);
        valueText.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        valueText.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        rowUi.PopStyle();
    }
}

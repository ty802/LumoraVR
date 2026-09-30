// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Assets;
using Lumora.Shading;

namespace Lumora.Core.Components.Assets;

// What a shader graph card's preview wears: one node's output on its own, unlit, compiled on this machine
// from the graph this machine has. Textures come from the material the graph belongs to, so a slot's
// texture shows in every preview it reaches; everything else shows the value typed on its node. Checked by
// the sandbox like any custom shader. Local, never saved. -xlinka
[ComponentCategory("Hidden")]
public sealed class ShaderPreviewMaterial : MaterialProvider
{
    private string _source = string.Empty;
    private IReadOnlyList<ShaderUniform> _uniforms = Array.Empty<ShaderUniform>();
    private ShaderGraphSource? _textures;
    private int _textureStamp;
    private string? _checkedSource;
    private bool _passes;

    protected override MaterialType MaterialType => MaterialType.Custom;

    public override bool UseLoadingPlaceholder => false;

    // The source shown now; empty when the node has nothing that compiles.
    public string Source => _source;

    public void Show(ShaderProgram? program, ShaderGraphSource? textures)
    {
        var source = program is { Succeeded: true } ? program.Source : string.Empty;
        if (source == _source && ReferenceEquals(textures, _textures))
            return;
        _source = source;
        _uniforms = program?.Uniforms ?? Array.Empty<ShaderUniform>();
        _textures = textures;
        _textureStamp = TextureStamp();
        MarkChangeDirty();
    }

    // A texture swapped on the material reaches the preview without the graph changing.
    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        int stamp = TextureStamp();
        if (stamp == _textureStamp)
            return;
        _textureStamp = stamp;
        MarkChangeDirty();
    }

    protected override void UpdateMaterial(MaterialAsset asset)
    {
        if (_checkedSource != _source)
        {
            _checkedSource = _source;
            _passes = _source.Length > 0 && ShaderSourceValidator.Validate(_source).IsValid;
        }
        asset.SetCustomShaderSource(_passes ? _source : string.Empty);
        if (!_passes)
            return;
        foreach (var uniform in _uniforms)
        {
            if (uniform.Type == ShaderType.Texture)
                asset.SetTexture(uniform.Name, _textures?.TextureFor(uniform.Name)?.Asset!);
        }
    }

    private int TextureStamp()
    {
        var hash = new HashCode();
        foreach (var uniform in _uniforms)
        {
            if (uniform.Type != ShaderType.Texture)
                continue;
            var texture = _textures?.TextureFor(uniform.Name);
            hash.Add(texture);
            hash.Add(texture?.IsAssetAvailable ?? false);
        }
        return hash.ToHashCode();
    }
}

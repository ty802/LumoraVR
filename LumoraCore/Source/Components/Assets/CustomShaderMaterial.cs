// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Helio.UI;
using Lumora.Nexus.Cloud.Cdn;
using Lumora.Core.Assets;
using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components.Assets;

[ComponentCategory("Assets/Materials")]
public sealed class CustomShaderMaterial : MaterialProvider, ICustomInspectorUI
{
    private sealed class UniformObserver
    {
        public Action<float4> ValueChanged = _ => { };
        public Action<ShaderUniformType> TypeChanged = _ => { };
        public Action<bool> IsColorChanged = _ => { };
        public Action<bool> HasRangeChanged = _ => { };
        public Action<float2> RangeChanged = _ => { };
        public ReferenceEvent<IAssetProvider<TextureAsset>> TextureChanged = _ => { };
    }

    public readonly AssetRef<ShaderSourceAsset> Shader;

    // built-in res:// .gdshader path; used instead of Shader for engine shaders
    public readonly Sync<string> ShaderPath;

    public readonly SyncList<ShaderUniformParam> Parameters;

    public readonly Sync<BlendMode> BlendMode;

    public readonly Sync<Culling> Culling;

    // -1 = default
    public readonly Sync<int> RenderQueue;

    private string _lastShaderHash = null!;
    private readonly Dictionary<ShaderUniformParam, UniformObserver> _uniformObservers = new();
    private bool _isUpdatingMaterial;
    // Sandbox verdict for the current source, cached by content hash (validation is pure static
    // analysis, no need to re-run per material update). Local only, never synced. - xlinka
    private ShaderSourceValidator.Result? _validation;
    private string? _validationHash;

    private ShaderSourceValidator.Result ValidateSource(string source)
    {
        var hash = ContentHash.FromString(source);
        if (_validation != null && hash == _validationHash)
        {
            return _validation;
        }
        _validationHash = hash;
        _validation = ShaderSourceValidator.Validate(source);
        if (!_validation.IsValid)
        {
            LumoraLogger.Warn($"CustomShaderMaterial: shader rejected by sandbox on '{Slot?.SlotName.Value}': {_validation.Errors[0]}"
                + (_validation.Errors.Count > 1 ? $" (+{_validation.Errors.Count - 1} more)" : ""));
        }
        return _validation;
    }

    protected override MaterialType MaterialType => MaterialType.Custom;

    public CustomShaderMaterial()
    {
        Shader = new AssetRef<ShaderSourceAsset>(this);
        ShaderPath = new Sync<string>(this, string.Empty);
        Parameters = new SyncList<ShaderUniformParam>();
        BlendMode = new Sync<BlendMode>(this, global::Lumora.Core.Assets.BlendMode.Opaque);
        Culling = new Sync<Culling>(this, global::Lumora.Core.Assets.Culling.Back);
        RenderQueue = new Sync<int>(this, -1);

        Parameters.ElementsAdded += OnParametersAdded;
        Parameters.ElementsRemoving += OnParametersRemoving;
    }

    protected override void UpdateMaterial(MaterialAsset asset)
    {
        _isUpdatingMaterial = true;
        try
        {
            asset.SetBlendMode(BlendMode.Value);
            asset.SetCulling(Culling.Value);
            asset.SetFloat("RenderQueue", RenderQueue.Value);

            var shaderPath = ShaderPath.Value;
            if (!string.IsNullOrWhiteSpace(shaderPath))
            {
                asset.SetCustomShader(shaderPath);
            }

            var shaderAsset = Shader.Asset;
            var shaderSource = shaderAsset?.Source;
            if (!string.IsNullOrWhiteSpace(shaderSource))
            {
                // Sandbox gate. Runs on EVERY peer right here because the source syncs: a remote user's
                // material is compiled by THIS client, so an import-time check alone is worthless. An
                // invalid shader never reaches the platform compile at all. ShaderPath (built-in res://
                // shaders) stays trusted and unvalidated. - xlinka
                var validation = ValidateSource(shaderSource!);
                if (validation.IsValid)
                {
                    if (string.IsNullOrWhiteSpace(shaderPath))
                    {
                        asset.SetCustomShaderSource(shaderSource);
                    }
                    EnsureUniforms(shaderSource!);
                }
                else if (string.IsNullOrWhiteSpace(shaderPath))
                {
                    // Rejected source: a prior update may have already compiled an older, valid
                    // version onto this asset. Never leave that stale compile rendering under a
                    // "REJECTED (not compiled)" verdict - drop back to the material's default
                    // (un-shaded) look. Empty source is the hook's "clear" signal. -xlinka
                    asset.SetCustomShaderSource(string.Empty);
                }
            }

            ApplyParameters(asset);
        }
        finally
        {
            _isUpdatingMaterial = false;
        }
    }

    private void EnsureUniforms(string shaderSource)
    {
        if (World == null || !World.IsAuthority)
        {
            return;
        }

        var hash = ContentHash.FromString(shaderSource);
        if (hash == _lastShaderHash)
        {
            return;
        }
        _lastShaderHash = hash;

        var existing = new Dictionary<string, ShaderUniformParamSnapshot>();
        foreach (var param in Parameters)
        {
            if (string.IsNullOrEmpty(param.Name.Value))
            {
                continue;
            }
            existing[param.Name.Value] = new ShaderUniformParamSnapshot(param);
        }

        Parameters.Clear();

        var defs = ShaderUniformParser.Parse(shaderSource);
        foreach (var def in defs)
        {
            var param = Parameters.Add();
            param.Name.Value = def.Name;
            param.Type.Value = def.Type;
            param.IsColor.Value = def.IsColor;
            param.HasRange.Value = def.HasRange;
            param.Range.Value = def.Range;

            if (existing.TryGetValue(def.Name, out var snapshot))
            {
                snapshot.Apply(param);
                continue;
            }

            if (def.HasDefault)
            {
                param.Value.Value = def.DefaultValue;
            }
        }

        LumoraLogger.Debug($"CustomShaderMaterial: Built {Parameters.Count} uniforms for shader");
    }

    private void OnParametersAdded(SyncElementList<ShaderUniformParam> list, int index, int count)
    {
        for (int i = 0; i < count; i++)
        {
            var param = list[index + i];
            AttachUniformObserver(param);
        }

        NotifyUniformChanged();
    }

    private void OnParametersRemoving(SyncElementList<ShaderUniformParam> list, int index, int count)
    {
        for (int i = 0; i < count; i++)
        {
            var param = list[index + i];
            DetachUniformObserver(param);
        }

        NotifyUniformChanged();
    }

    private void AttachUniformObserver(ShaderUniformParam param)
    {
        if (param == null || _uniformObservers.ContainsKey(param))
        {
            return;
        }

        var observer = new UniformObserver
        {
            ValueChanged = _ => NotifyUniformChanged(),
            TypeChanged = _ => NotifyUniformChanged(),
            IsColorChanged = _ => NotifyUniformChanged(),
            HasRangeChanged = _ => NotifyUniformChanged(),
            RangeChanged = _ => NotifyUniformChanged(),
            TextureChanged = _ => NotifyUniformChanged()
        };

        param.Value.OnChanged += observer.ValueChanged;
        param.Type.OnChanged += observer.TypeChanged;
        param.IsColor.OnChanged += observer.IsColorChanged;
        param.HasRange.OnChanged += observer.HasRangeChanged;
        param.Range.OnChanged += observer.RangeChanged;
        param.Texture.OnTargetChange += observer.TextureChanged;

        _uniformObservers[param] = observer;
    }

    private void DetachUniformObserver(ShaderUniformParam param)
    {
        if (param == null || !_uniformObservers.TryGetValue(param, out var observer))
        {
            return;
        }

        param.Value.OnChanged -= observer.ValueChanged;
        param.Type.OnChanged -= observer.TypeChanged;
        param.IsColor.OnChanged -= observer.IsColorChanged;
        param.HasRange.OnChanged -= observer.HasRangeChanged;
        param.Range.OnChanged -= observer.RangeChanged;
        param.Texture.OnTargetChange -= observer.TextureChanged;

        _uniformObservers.Remove(param);
    }

    private void NotifyUniformChanged()
    {
        if (_isUpdatingMaterial || IsDestroyed)
        {
            return;
        }

        MarkChangeDirty();
    }

    private void ApplyParameters(MaterialAsset asset)
    {
        foreach (var param in Parameters)
        {
            var name = param.Name.Value;
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            switch (param.Type.Value)
            {
                case ShaderUniformType.Float:
                    asset.SetFloat(name, param.Value.Value.x);
                    break;
                case ShaderUniformType.Vec2:
                    asset.SetFloat2(name, new float2(param.Value.Value.x, param.Value.Value.y));
                    break;
                case ShaderUniformType.Vec3:
                    asset.SetFloat3(name, new float3(param.Value.Value.x, param.Value.Value.y, param.Value.Value.z));
                    break;
                case ShaderUniformType.Vec4:
                    if (param.IsColor.Value)
                    {
                        asset.SetColor(name, new colorHDR(param.Value.Value.x, param.Value.Value.y, param.Value.Value.z, param.Value.Value.w));
                    }
                    else
                    {
                        asset.SetFloat4(name, new float4(param.Value.Value.x, param.Value.Value.y, param.Value.Value.z, param.Value.Value.w));
                    }
                    break;
                case ShaderUniformType.Int:
                    asset.SetInt(name, (int)param.Value.Value.x);
                    break;
                case ShaderUniformType.Bool:
                    asset.SetBool(name, param.Value.Value.x >= 0.5f);
                    break;
                case ShaderUniformType.Texture2D:
                    asset.SetTexture(name, param.Texture.Asset);
                    break;
            }
        }
    }

    // An imported shader has no albedo field for the eyedropper to read, so the only honest answer is
    // the first vec4 the AUTHOR tagged source_color - the parser already records that as IsColor. A
    // shader with no colour uniform at all returns false instead of handing back whichever vec4 happened
    // to be declared first. -xlinka
    public override bool TryGetPrimaryColor(out colorHDR color)
    {
        foreach (var param in Parameters)
        {
            if (param.Type.Value != ShaderUniformType.Vec4 || !param.IsColor.Value)
            {
                continue;
            }
            var value = param.Value.Value;
            color = new colorHDR(value.x, value.y, value.z, value.w);
            return true;
        }

        color = colorHDR.White;
        return false;
    }

    private readonly struct ShaderUniformParamSnapshot
    {
        private readonly ShaderUniformType _type;
        private readonly float4 _value;
        private readonly bool _isColor;
        private readonly bool _hasRange;
        private readonly float2 _range;
        private readonly IAssetProvider<TextureAsset>? _textureTarget;

        public ShaderUniformParamSnapshot(ShaderUniformParam param)
        {
            _type = param.Type.Value;
            _value = param.Value.Value;
            _isColor = param.IsColor.Value;
            _hasRange = param.HasRange.Value;
            _range = param.Range.Value;
            _textureTarget = param.Texture.Target;
        }

        public void Apply(ShaderUniformParam param)
        {
            param.Type.Value = _type;
            param.Value.Value = _value;
            param.IsColor.Value = _isColor;
            param.HasRange.Value = _hasRange;
            param.Range.Value = _range;
            if (_textureTarget != null)
            {
                param.Texture.Target = _textureTarget;
            }
        }
    }

    // Inspector diagnostics: the sandbox verdict plus every stat we can honestly compute from the
    // source and bound assets. No per-material GPU timings - the renderer does not expose them, and a
    // made-up number is worse than none. - xlinka
    public void BuildInspectorBody(UIBuilder ui)
    {
        var source = Shader.Asset?.Source;
        if (string.IsNullOrWhiteSpace(source))
        {
            AddStatRow(ui, "Shader", string.IsNullOrWhiteSpace(ShaderPath.Value) ? "no shader source" : $"built-in ({ShaderPath.Value})");
            return;
        }

        var v = ValidateSource(source!);
        AddStatRow(ui, "Sandbox", v.IsValid ? "passed" : "REJECTED (not compiled)");
        for (int i = 0; i < v.Errors.Count && i < 3; i++)
            AddStatRow(ui, i == 0 ? "Errors" : "", v.Errors[i]);
        if (v.Errors.Count > 3)
            AddStatRow(ui, "", $"+{v.Errors.Count - 3} more");
        for (int i = 0; i < v.Warnings.Count && i < 2; i++)
            AddStatRow(ui, i == 0 ? "Warnings" : "", v.Warnings[i]);

        AddStatRow(ui, "Shader type", string.IsNullOrEmpty(v.ShaderType) ? "unknown" : v.ShaderType);
        AddStatRow(ui, "Source", $"{v.SourceBytes / 1024f:0.#} KB");
        AddStatRow(ui, "Uniforms", v.UniformCount.ToString());
        AddStatRow(ui, "Texture samples", v.TextureSampleCount.ToString());
        if (v.LoopCount > 0)
            AddStatRow(ui, "Loops", $"{v.LoopCount} (max bound {v.MaxLoopBoundSeen})");

        // Sum only what we can actually account for. A texture whose renderer has reported its GPU
        // format contributes an exact byte count computed from that format, its dimensions and its
        // mip count; one that has not is counted as unmeasured rather than folded in behind a
        // fudge factor, and the row says how many were left out. A total that silently mixes
        // measurements with guesses is not a measurement. -xlinka
        long vramBytes = 0;
        int boundTextures = 0;
        int unmeasured = 0;
        foreach (var param in Parameters)
        {
            var tex = param.Texture.Asset;
            if (tex == null || tex.Width <= 0 || tex.Height <= 0)
                continue;
            boundTextures++;

            if (tex.Metadata?.GpuBytes is { } bytes)
                vramBytes += bytes;
            else
                unmeasured++;
        }
        if (boundTextures > 0)
        {
            string measured = unmeasured == 0
                ? $"{boundTextures} tex"
                : $"{boundTextures - unmeasured}/{boundTextures} tex measured";
            AddStatRow(ui, "Texture VRAM", $"{InspectorStats.Bytes(vramBytes)} ({measured})");
        }

        int refCount = 0;
        foreach (var _ in References)
            refCount++;
        AddStatRow(ui, "Referenced by", refCount.ToString());
    }

    private static void AddStatRow(UIBuilder ui, string label, string value)
    {
        // Theme from the hosting panel's UI tree, NOT this component's world slot: the material's
        // slot has no UITheme above it, and Helio text without a font renders nothing.
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

    public override void OnDestroy()
    {
        Parameters.ElementsAdded -= OnParametersAdded;
        Parameters.ElementsRemoving -= OnParametersRemoving;

        var keys = new List<ShaderUniformParam>(_uniformObservers.Keys);
        foreach (var param in keys)
        {
            DetachUniformObserver(param);
        }

        base.OnDestroy();
    }
}


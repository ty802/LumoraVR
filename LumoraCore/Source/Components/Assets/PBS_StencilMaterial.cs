// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Assets;

// Opaque PBS that tests and writes the stencil buffer, for portals, masks and see-through-walls effects: one
// material stamps a value where it draws, another draws only where that value is (or is not). ColorMask None
// makes a stamp that draws no colour while it still writes depth and stencil.
//
// Godot's stencil state is compile-time and small: one reference value for both the test and the write,
// writes that only ever store that value, and no read or write masks. So the shader is generated per
// configuration and compiled once per distinct source:
//   Replace stores StencilID; Zero, Invert, Increment and Decrement store what they would leave in a cleared
//   buffer (0, 255, 1, and 0 or 255), which is exact for the usual single stamp on a cleared buffer.
//   A material that both tests and writes stores its test value, whatever the operation.
//   The masks fold into the reference value: the test compares StencilID & StencilReadMask, a write stores
//   its value & StencilWriteMask. Exact while the buffer holds nothing outside the masks.
//   A colour mask that drops red, green and blue draws nothing; any other partial mask draws all four.
// A material that tests the stencil draws in the transparent pass (Godot refuses a stencil test in the opaque
// one) with full alpha and depth writes, so it still looks and occludes as an opaque surface. -xlinka
public abstract class PBS_StencilMaterial : MaterialProvider, ICommonMaterial
{
    public readonly Sync<float2> TextureScale;
    public readonly Sync<float2> TextureOffset;

    public readonly Sync<colorHDR> AlbedoColor;
    public readonly AssetRef<TextureAsset> AlbedoTexture;

    public readonly Sync<colorHDR> EmissiveColor;
    public readonly AssetRef<TextureAsset> EmissiveMap;

    public readonly AssetRef<TextureAsset> NormalMap;
    public readonly Sync<float> NormalScale;

    public readonly AssetRef<TextureAsset> OcclusionMap;

    public readonly Sync<ColorMask> ColorMask;
    public readonly Sync<StencilComparison> StencilComparison;
    public readonly Sync<StencilOperation> StencilOperation;
    public readonly Sync<byte> StencilID;
    public readonly Sync<byte> StencilWriteMask;
    public readonly Sync<byte> StencilReadMask;

    // Kept for what a package carries; the shader has no depth bias to give them.
    public readonly Sync<float> OffsetFactor;
    public readonly Sync<float> OffsetUnits;

    public readonly Sync<int> RenderQueue;

    private (bool Specular, ColorMask Mask, StencilComparison Comparison, StencilOperation Operation, byte Id, byte Write, byte Read) _sourceKey;
    private string? _source;

    protected override MaterialType MaterialType => MaterialType.PBS_Stencil;

    protected abstract bool Specular { get; }

    public colorHDR Color
    {
        get => AlbedoColor.Value;
        set => AlbedoColor.Value = value;
    }

    public IAssetProvider<TextureAsset> MainTexture
    {
        get => AlbedoTexture.Target;
        set => AlbedoTexture.Target = value;
    }

    protected PBS_StencilMaterial()
    {
        TextureScale = new Sync<float2>(this, float2.One);
        TextureOffset = new Sync<float2>(this, float2.Zero);

        AlbedoColor = new Sync<colorHDR>(this, colorHDR.White);
        AlbedoTexture = new AssetRef<TextureAsset>(this);

        EmissiveColor = new Sync<colorHDR>(this, colorHDR.Black);
        EmissiveMap = new AssetRef<TextureAsset>(this);

        NormalMap = new AssetRef<TextureAsset>(this);
        NormalScale = new Sync<float>(this, 1f);

        OcclusionMap = new AssetRef<TextureAsset>(this);

        ColorMask = new Sync<ColorMask>(this, Assets.ColorMask.RGBA);
        StencilComparison = new Sync<StencilComparison>(this, Assets.StencilComparison.Always);
        StencilOperation = new Sync<StencilOperation>(this, Assets.StencilOperation.Keep);
        StencilID = new Sync<byte>(this, 0);
        StencilWriteMask = new Sync<byte>(this, byte.MaxValue);
        StencilReadMask = new Sync<byte>(this, byte.MaxValue);

        OffsetFactor = new Sync<float>(this, 0f);
        OffsetUnits = new Sync<float>(this, 0f);
        RenderQueue = new Sync<int>(this, -1);
    }

    protected override void UpdateMaterial(MaterialAsset asset)
    {
        var key = (Specular, ColorMask.Value, StencilComparison.Value, StencilOperation.Value,
            StencilID.Value, StencilWriteMask.Value, StencilReadMask.Value);
        if (_source == null || key != _sourceKey)
        {
            _sourceKey = key;
            _source = BuildSource(key.Specular, key.Item2, key.Item3, key.Item4, key.Item5, key.Item6, key.Item7);
        }
        asset.SetCustomShaderSource(_source);
        asset.SetFloat("RenderQueue", RenderQueue.Value);

        asset.SetFloat2("TextureScale", TextureScale.Value);
        asset.SetFloat2("TextureOffset", TextureOffset.Value);

        asset.SetColor("AlbedoColor", AlbedoColor.Value);
        asset.SetTexture("AlbedoTexture", AlbedoTexture.Asset);

        asset.SetColor("EmissiveColor", EmissiveColor.Value);
        asset.SetTexture("EmissiveMap", EmissiveMap.Asset);

        asset.SetTexture("NormalMap", NormalMap.Asset);
        asset.SetFloat("NormalScale", NormalScale.Value);

        asset.SetTexture("OcclusionMap", OcclusionMap.Asset);

        UpdateWorkflow(asset);
    }

    protected abstract void UpdateWorkflow(MaterialAsset asset);

    internal static string BuildSource(bool specular, ColorMask colorMask, StencilComparison comparison,
        StencilOperation operation, byte id, byte writeMask, byte readMask)
    {
        bool never = comparison == Assets.StencilComparison.Never;
        bool enabled = !never && comparison != Assets.StencilComparison.Disabled;
        bool read = enabled && comparison != Assets.StencilComparison.Always;
        bool write = enabled && operation != Assets.StencilOperation.Keep;
        bool hidden = (colorMask & Assets.ColorMask.RGB) == Assets.ColorMask.None;
        bool transparent = read || hidden;

        var sb = new System.Text.StringBuilder(256);
        sb.Append("shader_type spatial;\n");
        sb.Append(transparent
            ? "render_mode blend_mix, cull_back, depth_draw_always;\n"
            : "render_mode blend_mix, cull_back;\n");

        if (read || write)
        {
            int reference = read ? id & readMask : StoredValue(operation, id) & writeMask;
            sb.Append("stencil_mode ");
            if (read)
                sb.Append("read, ");
            if (write)
                sb.Append("write, ");
            sb.Append(CompareMode(comparison)).Append(", ").Append(reference).Append(";\n");
        }

        sb.Append("#define PBS_STENCIL\n");
        if (transparent)
            sb.Append("#define TRANSPARENT_PASS\n");
        if (hidden)
            sb.Append("#define PBS_STENCIL_HIDDEN\n");
        if (never)
            sb.Append("#define PBS_STENCIL_NEVER\n");
        if (specular)
            sb.Append("#define PBS_SPECULAR\n");
        sb.Append("#include \"res://Shaders/PBS_Effect_Core.gdshaderinc\"\n");
        return sb.ToString();
    }

    // What an operation leaves in a cleared buffer.
    private static int StoredValue(StencilOperation operation, byte id) => operation switch
    {
        Assets.StencilOperation.Zero => 0,
        Assets.StencilOperation.IncrementSaturate or Assets.StencilOperation.IncrementWrap => 1,
        Assets.StencilOperation.DecrementSaturate => 0,
        Assets.StencilOperation.DecrementWrap or Assets.StencilOperation.Invert => 255,
        _ => id
    };

    private static string CompareMode(StencilComparison comparison) => comparison switch
    {
        Assets.StencilComparison.Less => "compare_less",
        Assets.StencilComparison.Equal => "compare_equal",
        Assets.StencilComparison.LessOrEqual => "compare_less_or_equal",
        Assets.StencilComparison.Greater => "compare_greater",
        Assets.StencilComparison.NotEqual => "compare_not_equal",
        Assets.StencilComparison.GreaterOrEqual => "compare_greater_or_equal",
        _ => "compare_always"
    };
}

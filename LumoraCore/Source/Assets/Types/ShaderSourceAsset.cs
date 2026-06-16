// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Text;
using System.Threading.Tasks;

namespace Lumora.Core.Assets;

/// <summary>
/// Asset containing shader source text, gathered and decoded from its URL.
/// </summary>
public sealed class ShaderSourceAsset : LoadableAsset
{
    private byte[]? _rawBytes;
    private string? _source;

    /// <summary>Raw shader source bytes.</summary>
    public byte[]? RawBytes => _rawBytes;

    /// <summary>Shader source text (UTF-8 decoded).</summary>
    public string? Source => _source;

    protected override async Task LoadSelf()
    {
        var bytes = await AssetManager.RequestGather(AssetURL).ConfigureAwait(false);
        if (bytes == null || bytes.Length == 0)
        {
            FailLoad($"No shader source data gathered for {AssetURL}");
            return;
        }

        _rawBytes = bytes;
        _source = Encoding.UTF8.GetString(bytes);
        Version++;
    }

    public override void Unload()
    {
        _rawBytes = null;
        _source = null;
    }
}

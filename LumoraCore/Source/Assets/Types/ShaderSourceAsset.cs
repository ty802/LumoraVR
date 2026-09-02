// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Text;
using System.Threading.Tasks;

namespace Lumora.Core.Assets;

// A shader as loaded from its URL: either a .lumshader bundle (the record form, with its includes and
// manifest inside) or a bare text file from before bundles existed. Either way Source is the one text
// the sandbox reads and the device compiles. -xlinka
public sealed class ShaderSourceAsset : LoadableAsset
{
    private byte[]? _rawBytes;
    private string? _source;
    private ShaderBundleInfo? _bundle;

    public byte[]? RawBytes => _rawBytes;

    public string? Source => _source;

    // The manifest of the bundle this came from; null for a bare text shader.
    public ShaderBundleInfo? Bundle => _bundle;

    protected override async Task LoadSelf()
    {
        var bytes = await AssetManager.RequestGather(AssetURL).ConfigureAwait(false);
        if (bytes == null || bytes.Length == 0)
        {
            FailLoad($"No shader source data gathered for {AssetURL}");
            return;
        }
        _rawBytes = bytes;
        if (ShaderBundle.IsBundle(bytes))
        {
            if (!ShaderBundle.TryOpen(bytes, out var info, out var text))
            {
                FailLoad($"Shader bundle at {AssetURL} could not be opened");
                return;
            }
            _bundle = info;
            _source = text;
        }
        else
        {
            _bundle = null;
            _source = Encoding.UTF8.GetString(bytes);
        }
        Version++;
    }

    public override void Unload()
    {
        _rawBytes = null;
        _source = null;
        _bundle = null;
    }
}

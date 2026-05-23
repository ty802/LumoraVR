// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Assets;

public sealed class FontMetadata : IAssetMetadata
{
    public long ByteLength { get; set; }

    public long EstimatedMemorySize => ByteLength;
}

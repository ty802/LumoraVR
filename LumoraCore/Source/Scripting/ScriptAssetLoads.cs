// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Assets;

namespace Lumora.Core.Scripting;

// Where an asset provider's load stands on this machine. A provider backed by an address loads when
// something uses it and says why when it fails; while its bytes come from another machine in the session
// the transfer says how far along it is. A provider made here (a generated mesh, a material) is there as
// soon as it exists, except that a material waits on its textures. -xlinka
internal static class ScriptAssetLoads
{
    public static bool Read(ScriptContext ctx, IAssetProvider? provider, out AssetLoadState state, out float progress, out string error)
    {
        state = AssetLoadState.Unloaded;
        progress = -1f;
        error = string.Empty;
        if (provider is not Component component || !ScriptReads.Visible(ctx, component))
            return false;

        if (provider is IUrlAssetProvider url)
        {
            if (url.LoadFailure is { } why)
            {
                state = AssetLoadState.Failed;
                error = why;
                return false;
            }
            if (provider.IsAssetAvailable)
            {
                state = provider.GenericAsset is Asset loaded ? loaded.LoadState : AssetLoadState.FullyLoaded;
                progress = 1f;
                return true;
            }
            if (url.SourceUrl == null)
                return false;
            if (provider.AssetReferenceCount == 0)
            {
                state = AssetLoadState.Created;
                return false;
            }
            state = AssetLoadState.LoadStarted;
            progress = Transferred(ctx, url.SourceUrl);
            return false;
        }

        if (AssetReadiness.IsPending(provider))
        {
            state = AssetLoadState.LoadStarted;
            return false;
        }
        if (!provider.IsAssetAvailable)
        {
            state = AssetLoadState.Created;
            return false;
        }
        state = AssetLoadState.FullyLoaded;
        progress = 1f;
        return true;
    }

    // How much of the address has come from another machine in the session, or -1 while nothing measures it.
    private static float Transferred(ScriptContext ctx, Uri address)
    {
        var transferer = ctx.World.Session?.AssetTransferer;
        if (transferer == null)
            return -1f;
        foreach (var transfer in transferer.GetActiveTransfers())
        {
            if (!transfer.IsUpload && transfer.Total > 0
                && string.Equals(transfer.Uri?.OriginalString, address.OriginalString, StringComparison.Ordinal))
                return System.Math.Clamp(transfer.Fraction, 0f, 1f);
        }
        return -1f;
    }
}

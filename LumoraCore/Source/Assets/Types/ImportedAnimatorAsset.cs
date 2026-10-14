// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Threading.Tasks;
using Lumora.Core.Assets.Interop.Unity;

namespace Lumora.Core.Assets;

public sealed class ImportedAnimatorAsset : LoadableAsset
{
    public UnityAnimatorProgram? Program { get; private set; }

    protected override async Task LoadSelf()
    {
        var bytes = await AssetManager.RequestGather(AssetURL).ConfigureAwait(false);
        if (bytes == null) { FailLoad("No animator data gathered."); return; }
        // Compiled here, off the world thread, so an arriving avatar does not stall a frame building its graph.
        try { var program = UnityAnimatorProgram.FromBytes(bytes); _ = program.Graph; Program = program; Version++; }
        catch (Exception ex) { FailLoad("Could not decode animator: " + ex.Message); }
    }

    public override void Unload() => Program = null;
}

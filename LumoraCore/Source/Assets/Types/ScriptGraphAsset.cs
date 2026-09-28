// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Threading.Tasks;
using Lumora.Script;

namespace Lumora.Core.Assets;

// A script graph as loaded from its URL: the graph.json a pack wrote, content addressed like a shader
// bundle, so a world full of copies of one scripted object points at one record. Parsed with the
// graph reader's caps, off the world thread; a graph that does not parse fails the load and keeps its
// reason for the host to show. -xlinka
public sealed class ScriptGraphAsset : LoadableAsset
{
    public const string Extension = ".loomgraph";

    public ScriptGraph? Graph { get; private set; }

    public string? LoadError { get; private set; }

    protected override async Task LoadSelf()
    {
        var bytes = await AssetManager.RequestGather(AssetURL).ConfigureAwait(false);
        if (bytes == null || bytes.Length == 0)
        {
            LoadError = "no graph data";
            FailLoad(LoadError);
            return;
        }

        try
        {
            Graph = await DecodeOffThread(() => ScriptGraph.Parse(bytes)).ConfigureAwait(false);
            LoadError = null;
        }
        catch (GraphFormatException ex)
        {
            Graph = null;
            LoadError = "it does not parse: " + ex.Message;
            FailLoad(LoadError);
            return;
        }
        Version++;
    }

    public override void Unload()
    {
        Graph = null;
        LoadError = null;
    }

    // Writes a graph into this machine's asset store and answers the URL a provider points at. The same
    // graph always lands on the same URL.
    public static async Task<Uri?> StoreAsync(ScriptGraph graph)
    {
        var localDb = Engine.Current?.LocalDB;
        if (localDb == null)
            return null;
        var url = await localDb.SaveAssetAsync(graph.ToUtf8Json(), Extension).ConfigureAwait(false);
        return url == null ? null : new Uri(url);
    }
}

// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.
using Lumora.Core.Components.Interaction;
using Lumora.Core.Math;

namespace Lumora.Core.Components;

// The grab action on a tab tears it off into its own inspector, already in the hand. A panel's only tab
// declines, so gripping it moves the whole panel like any other spot on the frame.
[ComponentCategory("Hidden")]
public sealed class InspectorTabGrip : Component, IProxySource
{
    public readonly SyncRef<SceneInspectorPanel> Panel = new();
    public readonly SyncRef<InspectorTab> Tab = new();

    public IGrabbable? TryCreateProxy(Grabber grabber, in float3 spawnPoint)
    {
        var panel = Panel.Target;
        var tab = Tab.Target;
        if (panel == null || panel.IsDestroyed || tab == null || tab.IsDestroyed)
            return null;
        return panel.TearOffTab(tab);
    }
}

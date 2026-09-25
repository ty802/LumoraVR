// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.
using System.Linq;

namespace Lumora.Core.Components;

// A builder's note on one component, shown at the top of its inspector section and saved with the object.
[HideInInspector]
[ComponentCategory("Hidden")]
public sealed class InspectorComponentNote : Component
{
    public readonly SyncRef<Component> Target = new();
    public readonly Sync<string> Note = new();

    public static InspectorComponentNote? Find(Component target)
        => target.Slot.GetComponents<InspectorComponentNote>().FirstOrDefault(n => n.Target.Target == target);
}

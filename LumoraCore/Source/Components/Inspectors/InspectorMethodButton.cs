// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Reflection;
using Helio.UI;
using Lumora.Core;
using Lumora.Core.Logging;

namespace Lumora.Core.Components;

// invokes the named public parameterless [SyncMethod] method on the target component. the
// attribute is re-checked at invoke time, same gate the delegate resolution path uses, so a
// renamed row can never call into an arbitrary method. exceptions are logged and contained.
[ComponentCategory("Utility/Inspectors")]
public class InspectorMethodButton : Component
{
    public readonly SyncRef<Component> Target;
    public readonly Sync<string> MethodName;

    public InspectorMethodButton()
    {
        Target = new SyncRef<Component>(this);
        MethodName = new Sync<string>(this, "");
    }

    [SyncMethod]
    public void OnPressed(Button button, UIInteractionContext context)
    {
        var target = Target.Target;
        string name = MethodName.Value;
        if (target == null || target.IsDestroyed || string.IsNullOrEmpty(name))
            return;

        var method = target.GetType().GetMethod(name, BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
        if (method == null || method.GetCustomAttribute<SyncMethodAttribute>() == null)
            return;

        try
        {
            method.Invoke(target, null);
        }
        catch (Exception ex)
        {
            Logger.Error($"Inspector method row '{target.GetType().Name}.{name}' threw: {ex.InnerException ?? ex}");
        }
    }
}

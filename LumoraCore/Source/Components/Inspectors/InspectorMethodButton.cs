// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Reflection;
using Helio.UI;
using Lumora.Core;
using Lumora.Core.Logging;

namespace Lumora.Core.Components;

// invokes the named public void [SyncMethod] method on the target component, with one value per argument
// row parsed against its parameter. the attribute and the parameter types are re-checked at invoke time,
// same gate the delegate resolution path uses, so a renamed row can never call into an arbitrary method.
// a value that does not parse refuses the press. exceptions are logged and contained.
[ComponentCategory("Utility/Inspectors")]
public class InspectorMethodButton : Component
{
    public readonly SyncRef<Component> Target;
    public readonly Sync<string> MethodName;
    public readonly SyncRefList<InspectorMethodArgument> Arguments = new();

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

        int count = Arguments.Count;
        var method = FindMethod(target.GetType(), name, count);
        if (method == null)
            return;

        var parameters = method.GetParameters();
        var values = new object?[count];
        for (int i = 0; i < count; i++)
        {
            var argument = Arguments[i];
            string text = argument?.Value.Value ?? "";
            if (argument == null || !InspectorMethodArgument.TryParse(text, parameters[i].ParameterType, out values[i]))
            {
                Logger.Warn($"Inspector method row '{target.GetType().Name}.{name}': '{text}' is not a {parameters[i].ParameterType.Name} for {parameters[i].Name}");
                return;
            }
        }

        try
        {
            method.Invoke(target, values);
        }
        catch (Exception ex)
        {
            Logger.Error($"Inspector method row '{target.GetType().Name}.{name}' threw: {ex.InnerException ?? ex}");
        }
    }

    // The public void [SyncMethod] of that name taking exactly count parameters, every one of a type a row
    // can carry.
    public static MethodInfo? FindMethod(Type type, string name, int count)
    {
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (method.Name != name || method.IsSpecialName || method.ReturnType != typeof(void))
                continue;
            var parameters = method.GetParameters();
            if (parameters.Length != count || !InspectorMethodArgument.AreSupported(parameters))
                continue;
            if (method.GetCustomAttribute<SyncMethodAttribute>() == null)
                continue;
            return method;
        }
        return null;
    }
}

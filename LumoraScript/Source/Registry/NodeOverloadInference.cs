// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Lumora.Script;

// Candidates for an incoming wire. The editor checks the rest of the graph before choosing one.
public static class NodeOverloadInference
{
    public static IEnumerable<NodeOverload> ForInput(NodeRegistry registry, NodeOverload current, string input, ScriptType given)
    {
        var definition = current.Definition;
        if (current.ConnectableInputIndex(input) < 0 || FunctionSignature.IsFunctionNode(definition.Kind) || definition.IsScriptCall)
            yield break;

        if (!definition.IsGeneric)
        {
            foreach (var binary in definition.KnownOverloads)
            {
                if (binary.Folds != null)
                    continue;
                var candidate = binary;
                if (current.Folds != null)
                {
                    if (binary.Inputs.Count != 2 || binary.Inputs[0].Type != binary.Inputs[1].Type
                        || binary.Outputs.Count != 1 || binary.Outputs[0].Type != binary.Inputs[0].Type
                        || !registry.TryResolve(definition,
                            string.Join(",", Enumerable.Repeat(binary.Inputs[0].Type.Name, current.Inputs.Count)),
                            out candidate, out _))
                        continue;
                }
                if (candidate.ConnectableInputIndex(input) is int index and >= 0 && candidate.Inputs[index].Type == given)
                    yield return candidate;
            }
            yield break;
        }

        if (current.Method is { } method)
        {
            // Read the open declaration so TKey and TValue remain distinct even when both currently
            // happen to be float. Untouched type arguments keep their current values.
            var open = definition.ClrType.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .FirstOrDefault(m => m.MetadataToken == method.MetadataToken && m.Module == method.Module);
            var parameter = open?.GetParameters().FirstOrDefault(p => !p.IsOut
                && NodePort.NormalizeName(p.GetCustomAttribute<PortAttribute>()?.Name ?? p.Name ?? "") == input);
            var inferred = new ScriptType?[definition.GenericArity];
            if (parameter == null || !Unify(parameter.ParameterType, given.ClrType, inferred) || inferred.All(t => t == null))
                yield break;
            string signature = string.Join(",", inferred.Select((t, i) => (t ?? current.TypeArguments[i]).Name));
            if (registry.TryResolve(definition, signature, out var candidate, out _)
                && candidate.ConnectableInputIndex(input) is int index and >= 0 && candidate.Inputs[index].Type == given)
                yield return candidate;
            yield break;
        }

        // Built-in relay/display and Build-declared flow/memory nodes have no Run method. Resolve
        // their shapes with the incoming type (and its collection arguments), then check the actual port.
        if (definition.GenericArity == 1)
        {
            foreach (var type in Parts(given).Distinct())
            {
                if (registry.TryResolve(definition, type.Name, out var candidate, out _)
                    && candidate.ConnectableInputIndex(input) is int index and >= 0 && candidate.Inputs[index].Type == given)
                    yield return candidate;
            }
        }
    }

    private static bool Unify(Type pattern, Type given, ScriptType?[] arguments)
    {
        if (pattern.IsGenericParameter)
        {
            int index = pattern.GenericParameterPosition;
            if (index >= arguments.Length || !ScriptTypes.TryGet(given, out var type))
                return false;
            if (arguments[index] != null && arguments[index] != type)
                return false;
            arguments[index] = type;
            return true;
        }
        if (!pattern.ContainsGenericParameters)
            return pattern == given;
        if (!pattern.IsGenericType || !given.IsGenericType || pattern.GetGenericTypeDefinition() != given.GetGenericTypeDefinition())
            return false;
        var patterns = pattern.GetGenericArguments();
        var types = given.GetGenericArguments();
        for (int i = 0; i < patterns.Length; i++)
        {
            if (!Unify(patterns[i], types[i], arguments))
                return false;
        }
        return true;
    }

    private static IEnumerable<ScriptType> Parts(ScriptType type)
    {
        yield return type;
        foreach (var argument in type.Arguments)
        foreach (var part in Parts(argument))
            yield return part;
    }
}

// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Components;
using Lumora.Core.Localization;

namespace Lumora.Core.Scripting;

// A host's bindings before and after a pack, put back whole: a pack renumbers them all at once.
internal sealed class ScriptBindingsUndo : IUndoBatch
{
    private readonly SyncRefList<IWorldElement> _list;
    private readonly IWorldElement?[] _before;
    private readonly IWorldElement?[] _after;
    private readonly LocaleText _description;
    private readonly bool _rebuild;

    public ScriptBindingsUndo(SyncRefList<IWorldElement> list, IWorldElement?[] before, IWorldElement?[] after, LocaleText description,
        bool rebuild)
    {
        _list = list;
        _before = before;
        _after = after;
        _description = description;
        _rebuild = rebuild;
    }

    public string Description => _description.Resolve();

    public LocaleText LocalizedDescription => _description;

    public bool Undo() => Apply(_before);

    public bool Redo() => Apply(_after);

    public void OnEvicted()
    {
    }

    public static IWorldElement?[] Snapshot(SyncRefList<IWorldElement> list)
    {
        var targets = new IWorldElement?[list.Count];
        for (int i = 0; i < targets.Length; i++)
            targets[i] = list[i];
        return targets;
    }

    // Rebuilt when the assigner may remove entries, so each one is minted by them: a user's host refuses to
    // run on bindings someone other than its author put there. Otherwise patched in place, extra entries
    // blanked; a host only hands a script as many bindings as its graph declares.
    public static void Assign(SyncRefList<IWorldElement> list, IReadOnlyList<IWorldElement?> targets, bool rebuild)
    {
        if (rebuild)
        {
            while (list.Count > 0)
                list.RemoveAt(list.Count - 1);
            foreach (var target in targets)
                list.Add(target);
            return;
        }

        for (int i = 0; i < list.Count; i++)
        {
            var wanted = i < targets.Count ? targets[i] : null;
            if (!ReferenceEquals(list[i], wanted))
                list.GetElement(i).Target = wanted!;
        }
        for (int i = list.Count; i < targets.Count; i++)
            list.Add(targets[i]);
    }

    private bool Apply(IWorldElement?[] targets)
    {
        if (_list.IsDestroyed)
            return false;
        Assign(_list, targets, _rebuild);
        return true;
    }
}

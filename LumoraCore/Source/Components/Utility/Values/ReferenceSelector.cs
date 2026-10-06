// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Utility;

// Drives the reference the index picks. Works when something it reads changed: the index, an entry, or
// somebody writing over the reference it drives.
[ComponentCategory("Utility/Values")]
[DefaultUpdateOrder(-100)]
[ComponentGenericTypes(GenericTypeGroup.WorldElements, typeof(Component))]
public class ReferenceSelector<T> : Component where T : class, IWorldElement
{
    // Out-of-range indices wrap.
    public readonly Sync<int> Index;

    public readonly SyncRefList<T> References;

    public readonly DriveRef<T> Target;

    private ChangeFollow? _follow;

    public ReferenceSelector()
    {
        Index = new Sync<int>(this, 0);
        References = new SyncRefList<T>();
        Target = new DriveRef<T>(this) { LocalValueOnly = true };
    }

    // Null when the list is empty.
    public T? Selected
    {
        get
        {
            int count = References.Count;
            return count == 0 ? null : References[SelectionIndex.Wrap(Index.Value, count)];
        }
    }

    public override void OnDestroy()
    {
        _follow?.Clear();
        base.OnDestroy();
    }

    public override void OnChanges()
    {
        base.OnChanges();
        var follow = _follow ??= new ChangeFollow(this);
        follow.Begin();
        follow.AddList(References);
        for (int i = 0; i < References.Count; i++)
            follow.AddRef(References.GetReference(i));
        follow.Add(Target.Target);
        follow.End();

        if (References.Count == 0)
            return;
        follow.BeginQuiet();
        try
        {
            Target.SetValue(Selected);
        }
        finally
        {
            follow.EndQuiet();
        }
    }
}

// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Localization;

namespace Lumora.Core.Components;

// One curve gesture on a component field, undone by putting the whole list back as it was.
public sealed class FieldCurveUndoBatch : IUndoBatch
{
    private readonly FieldCurveSource _source;
    private readonly List<CurveKeyRow> _before;
    private readonly List<CurveKeyRow> _after;
    private readonly LocaleText _step;

    public FieldCurveUndoBatch(FieldCurveSource source, IReadOnlyList<CurveKeyRow> before, IReadOnlyList<CurveKeyRow> after, LocaleText step)
    {
        _source = source;
        _before = CurveKeyRow.CopyAll(before);
        _after = CurveKeyRow.CopyAll(after);
        _step = step;
    }

    public string Description => _step.Resolve();

    public LocaleText LocalizedDescription => _step;

    public bool Undo() => _source.Restore(_before);

    public bool Redo() => _source.Restore(_after);

    public void OnEvicted()
    {
    }
}

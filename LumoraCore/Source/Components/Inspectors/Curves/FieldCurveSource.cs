// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Localization;
using Lumora.Core.Networking.Sync;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components;

// A curve that lives in a component's own fields: points on 0..1, straight lines between them, flat
// past the ends. These are plain synced lists, so a drag writes straight through and everyone watches
// the point move; the release makes the whole gesture one undo step that puts the list back. The list
// is always written back sorted by time, which is how every one of these fields reads itself anyway.
public abstract class FieldCurveSource : ICurveSource
{
    protected readonly Component Owner;
    private readonly string _member;
    private readonly CurveTrackInfo[] _tracks;

    protected FieldCurveSource(Component owner, string member, string[] channels, bool whole)
    {
        Owner = owner;
        _member = Spaced(member);
        _tracks = new[]
        {
            new CurveTrackInfo
            {
                Group = OwnerName(owner),
                Name = _member,
                Channels = channels,
                Whole = whole,
                AllowHold = false,
                AllowSmooth = false,
            },
        };
    }

    // What the field reads as with no points at all.
    protected abstract float EmptyValue { get; }

    protected abstract SyncElement ListElement { get; }

    protected abstract List<CurveKeyRow> ReadRows();

    protected abstract void WriteRows(IReadOnlyList<CurveKeyRow> rows);

    protected abstract long Hash();

    public string Title => $"{OwnerName(Owner)} · {_member}";

    // The object it sits on reads better than the component type ("Tail Physbone", not "DynamicBoneChain").
    private static string OwnerName(Component owner)
    {
        string name = owner.Slot?.Name.Value ?? "";
        return string.IsNullOrWhiteSpace(name) ? owner.GetType().Name : name;
    }

    // "GravityCurve" -> "Gravity curve".
    private static string Spaced(string member)
    {
        var text = new System.Text.StringBuilder(member.Length + 4);
        for (int i = 0; i < member.Length; i++)
        {
            char c = member[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(member[i - 1]))
            {
                text.Append(' ');
                c = char.ToLowerInvariant(c);
            }
            text.Append(c);
        }
        return text.ToString();
    }

    public bool IsAlive => !Owner.IsDestroyed && !ListElement.IsDestroyed;

    public bool IsReady => IsAlive;

    public long Version => IsAlive ? Hash() : 0;

    public bool CanEdit => IsAlive && !ListElement.IsConfigLocked;

    public string ReadOnlyReason => !IsAlive ? "the field is gone" : ListElement.IsConfigLocked ? "host-configured, edit it in Session" : "";

    public IReadOnlyList<CurveTrackInfo> Tracks => _tracks;

    public float Duration => 1f;

    public bool FixedTimeRange => true;

    public float SnapStep => 0.05f;

    public List<CurveKeyRow> ReadKeys(int track)
    {
        var rows = IsAlive ? ReadRows() : new List<CurveKeyRow>();
        rows.Sort((a, b) => a.Time.CompareTo(b.Time));
        return rows;
    }

    public ICurveSampler CreateSampler(int track, IReadOnlyList<CurveKeyRow> rows)
        => new LinearRowSampler(rows, _tracks[0].Channels.Length, EmptyValue);

    public void Preview(int track, IReadOnlyList<CurveKeyRow> rows)
    {
        if (!CanEdit)
            return;
        try
        {
            WriteRows(rows);
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public void Commit(int track, IReadOnlyList<CurveKeyRow> before, IReadOnlyList<CurveKeyRow> after, LocaleText step, Action<string> report)
    {
        if (!CanEdit)
        {
            report(ReadOnlyReason);
            return;
        }
        try
        {
            WriteRows(after);
        }
        catch (UnauthorizedAccessException)
        {
            report("you may not change this field here");
            return;
        }
        InspectorUndo.Record(Owner, new FieldCurveUndoBatch(this, before, after, step));
        report("");
    }

    // For the undo step: false when the field is gone or refuses the write.
    internal bool Restore(IReadOnlyList<CurveKeyRow> rows)
    {
        if (!CanEdit)
            return false;
        try
        {
            WriteRows(rows);
            return true;
        }
        catch (UnauthorizedAccessException ex)
        {
            LumoraLogger.Warn($"FieldCurveSource: undo on {Title} refused: {ex.Message}");
            return false;
        }
    }

    public void Dispose()
    {
    }

    protected static long Mix(long hash, float value) => unchecked(hash * 31 + BitConverter.SingleToInt32Bits(value));
}

// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections;
using Helio.UI;
using Helio.UI.Layout;
using Lumora.Core;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Math;

namespace Lumora.Core.Components;

// rendered INLINE in the member flow: one full-width centered "Name (list):" label row, then one
// compact row per element (index, the element's editor stretched across the row, remove X), then a
// full-width Add row. small lists always show their elements; past 20 the section starts as a
// single centered "show N items" row so huge lists cost nothing until asked for, and rendering caps
// at 100 rows with an "N more" note. element-bearing lists are fully editable; flat value
// collections (SyncArray, SyncValueDictionary) render read-only since their entries are raw values,
// not sync members.
[ComponentCategory("Utility/Inspectors")]
public class ListMemberEditor : Component, IInspectorActionHandler
{
    public readonly SyncRef<IWorldElement> TargetList;
    public readonly Sync<bool> Expanded;
    public readonly Sync<string> MemberName;

    private readonly SyncRef<Slot> _body;

    private const int MaxRows = 100;
    // Lists at or under this size always render inline; only bigger ones start folded.
    private const int AutoCollapseThreshold = 20;

    private bool _hooked;
    private bool _listDirty;
    private bool _builtOnce;
    private bool _lastBuiltExpanded;

    public ListMemberEditor()
    {
        TargetList = new SyncRef<IWorldElement>(this);
        Expanded = new Sync<bool>(this, false);
        MemberName = new Sync<string>(this, "");
        _body = new SyncRef<Slot>(this);
    }

    private ISyncList? List => TargetList.Target as ISyncList;

    // true for the flat value collections this editor renders read-only. Walks the hierarchy so a
    // derived array (SyncGrid) is recognised too, instead of falling through to the one-line
    // "(TypeName)" row a top-level member would otherwise get.
    public static bool IsValueCollection(ISyncMember member)
    {
        for (var type = member.GetType(); type != null; type = type.BaseType)
        {
            if (!type.IsGenericType)
                continue;
            var definition = type.GetGenericTypeDefinition();
            if (definition == typeof(SyncArray<>) || definition == typeof(SyncValueDictionary<,>) || definition == typeof(SyncBag<>))
                return true;
        }
        return false;
    }

    public static void Build(ISyncMember member, string name, Slot container, Slot themeContext)
    {
        if (member is not IWorldElement listElement)
            return;

        // Wrapper + body are plain vertical stacks; their metrics aggregate the fixed-height rows,
        // so the section grows and shrinks with its content instead of squatting at a default rect.
        var wrapper = container.AddSlot(name);
        wrapper.AttachComponent<RectTransform>();
        var wrapperLayout = wrapper.AttachComponent<VerticalLayout>();
        wrapperLayout.Spacing.Value = 2f;
        wrapperLayout.ForceExpandWidth.Value = true;
        wrapperLayout.ForceExpandHeight.Value = false;

        var editor = wrapper.AttachComponent<ListMemberEditor>();
        editor.TargetList.Target = listElement;
        editor.MemberName.Value = name;

        // Label row: just the centered name, tinted by kind. Grip still pulls the list member.
        var header = InspectorUI.FixedRow(wrapper, "Header", InspectorUI.RowHeight, out var ui, themeContext);
        var headerSource = header.AttachComponent<ReferenceProxySource>();
        headerSource.Target.Target = listElement;

        InspectorUI.MemberChip(ui, SyncMemberEditorBuilder.MemberKindColor(member, driven: false), headerSource);

        ui.PushStyle();
        ui.FlexibleWidth(1f);
        var label = ui.Text($"{name} (list):", InspectorUI.FontSize, InspectorUI.TextColor);
        InspectorUI.FillParent(label.RectTransform!);
        label.HorizontalAlignment.Value = TextHorizontalAlignment.Center;
        label.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        ui.PopStyle();

        var body = wrapper.AddSlot("Body");
        body.AttachComponent<RectTransform>();
        var bodyLayout = body.AttachComponent<VerticalLayout>();
        bodyLayout.Spacing.Value = 2f;
        bodyLayout.ForceExpandWidth.Value = true;
        bodyLayout.ForceExpandHeight.Value = false;
        editor._body.Target = body;

        editor.HookListEvents();
    }

    public override void OnStart()
    {
        base.OnStart();
        HookListEvents();
    }

    public override void OnDestroy()
    {
        UnhookListEvents();
        base.OnDestroy();
    }

    private void HookListEvents()
    {
        if (_hooked)
            return;
        var target = TargetList.Target;
        if (target == null)
            return;
        _hooked = true;
        if (target is ISyncList list)
        {
            list.ElementsAdded += OnListElements;
            list.ElementsRemoved += OnListElements;
            list.ListCleared += OnListCleared;
        }
        else if (target is IChangeable changeable)
        {
            changeable.Changed += OnCollectionChanged;
        }
    }

    private void UnhookListEvents()
    {
        if (!_hooked)
            return;
        _hooked = false;
        var target = TargetList.Target;
        if (target is ISyncList list)
        {
            list.ElementsAdded -= OnListElements;
            list.ElementsRemoved -= OnListElements;
            list.ListCleared -= OnListCleared;
        }
        else if (target is IChangeable changeable)
        {
            changeable.Changed -= OnCollectionChanged;
        }
    }

    // Flag-and-defer: list events fire mid-mutation, so the rebuild happens in OnChanges, never here.
    private void OnListElements(ISyncList list, int index, int count)
    {
        _listDirty = true;
        MarkChangeDirty();
    }

    private void OnListCleared(ISyncList list)
    {
        _listDirty = true;
        MarkChangeDirty();
    }

    private void OnCollectionChanged(IChangeable changeable)
    {
        _listDirty = true;
        MarkChangeDirty();
    }

    // Small lists ignore the fold state entirely; only big ones honor it.
    private bool EffectiveExpanded()
        => CollectionCount() <= AutoCollapseThreshold || Expanded.Value;

    public override void OnChanges()
    {
        base.OnChanges();
        if (World?.IsAuthority != true)
            return;

        var body = _body.Target;
        if (body == null || body.IsDestroyed)
            return;

        bool expanded = EffectiveExpanded();
        if (!_builtOnce || _listDirty || expanded != _lastBuiltExpanded)
        {
            _builtOnce = true;
            _listDirty = false;
            _lastBuiltExpanded = expanded;
            RebuildBody(body, expanded);
        }
    }

    private int CollectionCount()
    {
        var target = TargetList.Target;
        if (target is ISyncList list)
            return list.Count;
        if (target == null)
            return 0;
        // Flat value collections expose Count without a shared interface.
        var property = target.GetType().GetProperty("Count");
        return property?.GetValue(target) is int n ? n : 0;
    }

    private void RebuildBody(Slot body, bool expanded)
    {
        body.DestroyChildren();

        var target = TargetList.Target;
        if (target == null)
            return;

        int total = CollectionCount();

        if (!expanded)
        {
            BuildActionRow(body, "ShowRow", $"show {total} items", "toggle");
            if (target is ISyncList)
                BuildActionRow(body, "AddRow", "Add", "add");
            return;
        }

        if (target is ISyncList list)
        {
            int shown = System.Math.Min(total, MaxRows);
            for (int i = 0; i < shown; i++)
                BuildElementRow(body, i, list.GetElement(i));
            if (total > shown)
                BuildNoteRow(body, $"{total - shown} more...");
            BuildActionRow(body, "AddRow", "Add", "add");
            return;
        }

        if (target is IEnumerable enumerable)
        {
            int index = 0;
            foreach (var value in enumerable)
            {
                if (index >= MaxRows)
                    break;
                BuildValueRow(body, index, value);
                index++;
            }
            if (total > MaxRows)
                BuildNoteRow(body, $"{total - MaxRows} more...");
        }
    }

    // Editable element: "i:" index, the element's editor stretched across the row, remove X.
    private void BuildElementRow(Slot body, int index, ISyncMember element)
    {
        var row = InspectorUI.FixedRow(body, $"E{index}", InspectorUI.RowHeight, out var ui, Slot);
        if (element is IWorldElement elementRef)
        {
            var source = row.AttachComponent<ReferenceProxySource>();
            source.Target.Target = elementRef;
        }

        BuildIndexLabel(ui, index);

        ui.PushStyle();
        ui.FlexibleWidth(1f);
        var editorArea = ui.Next("Editor");
        var areaLayout = editorArea.AttachComponent<HorizontalLayout>();
        areaLayout.Spacing.Value = 4f;
        areaLayout.ForceExpandHeight.Value = true;
        ui.NestInto(editorArea);
        SyncMemberEditorBuilder.BuildEditor(element, null, ui, editorArea);
        ui.NestOut();
        ui.PopStyle();

        ui.PushStyle();
        ui.TextColor(InspectorUI.AccentColor);
        InspectorUI.RelayButton(ui, this, $"up:{index}", "▲", 24f);
        InspectorUI.RelayButton(ui, this, $"down:{index}", "▼", 24f);
        ui.PopStyle();

        ui.PushStyle();
        ui.TextColor(InspectorUI.DangerColor);
        InspectorUI.RelayButton(ui, this, $"remove:{index}", "X", 26f);
        ui.PopStyle();
    }

    // Read-only value entry (SyncArray element, dictionary pair): index and the formatted value.
    private void BuildValueRow(Slot body, int index, object? value)
    {
        InspectorUI.FixedRow(body, $"V{index}", InspectorUI.RowHeight, out var ui, Slot);

        BuildIndexLabel(ui, index);

        ui.PushStyle();
        ui.FlexibleWidth(1f);
        var text = ui.Text(PrimitiveMemberEditor.FormatValue(value), InspectorUI.FontSize, InspectorUI.TextColor);
        InspectorUI.FillParent(text.RectTransform!);
        text.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        text.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        ui.PopStyle();
    }

    private static void BuildIndexLabel(UIBuilder ui, int index)
    {
        ui.PushStyle();
        ui.MinWidth(34f);
        ui.PreferredWidth(34f);
        ui.FlexibleWidth(0f);
        var indexLabel = ui.Text($"{index}:", InspectorUI.FontSize, InspectorUI.MutedColor);
        InspectorUI.FillParent(indexLabel.RectTransform!);
        indexLabel.HorizontalAlignment.Value = TextHorizontalAlignment.Right;
        indexLabel.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        ui.PopStyle();
    }

    // Full-width single-action rows: "Add" and the folded "show N items".
    private void BuildActionRow(Slot body, string rowName, string label, string action)
    {
        InspectorUI.FixedRow(body, rowName, InspectorUI.RowHeight, out var ui, Slot);
        ui.PushStyle();
        ui.TextColor(InspectorUI.AccentColor);
        InspectorUI.RelayButton(ui, this, action, label, 0f);
        ui.PopStyle();
    }

    private void BuildNoteRow(Slot body, string note)
    {
        InspectorUI.FixedRow(body, "Note", 24f, out var ui, Slot);
        ui.PushStyle();
        ui.FlexibleWidth(1f);
        var text = ui.Text(note, InspectorUI.FontSize - 1f, InspectorUI.MutedColor);
        InspectorUI.FillParent(text.RectTransform!);
        text.HorizontalAlignment.Value = TextHorizontalAlignment.Center;
        text.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        ui.PopStyle();
    }

    // action strings: "toggle" (unfold a big list), "add", "remove:<index>", "up:<index>"/
    // "down:<index>" (reorder). every structural edit goes through the undo history as one step;
    // element VALUE edits undo through the regular editor paths.
    public void HandleInspectorAction(string argument)
    {
        if (argument == "toggle")
        {
            Expanded.Value = !Expanded.Value;
            return;
        }
        if (argument == "add")
        {
            InspectorUndo.Record(this, ListElementUndoBatch.Add(List, out _, MarkListDirty));
            return;
        }
        if (argument.StartsWith("remove:", StringComparison.Ordinal) && int.TryParse(argument[7..], out int index))
        {
            InspectorUndo.Record(this, ListElementUndoBatch.Remove(List, index, MarkListDirty));
            return;
        }
        if (argument.StartsWith("up:", StringComparison.Ordinal) && int.TryParse(argument[3..], out int upIndex))
        {
            MoveElement(upIndex, upIndex - 1);
            return;
        }
        if (argument.StartsWith("down:", StringComparison.Ordinal) && int.TryParse(argument[5..], out int downIndex))
        {
            MoveElement(downIndex, downIndex + 1);
        }
    }

    private void MoveElement(int from, int to)
    {
        var batch = ListElementUndoBatch.Move(List, from, to, MarkListDirty);
        if (batch == null)
            return;
        InspectorUndo.Record(this, batch);
        MarkListDirty();
    }

    // re-renders the element rows. a reorder that swapped two element VALUES fires no list event, so
    // nothing else would notice it - and a row whose TextInput is FOCUSED ignores element changes
    // anyway (its editor early-outs while typing) and would later commit the stale text back over
    // the swap, silently reverting it. the rebuild tears the focused editor down.
    internal void MarkListDirty()
    {
        if (IsDestroyed)
            return;
        _listDirty = true;
        MarkChangeDirty();
    }
}

// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Helio.UI;
using Helio.UI.Layout;
using Lumora.Core.Assets;
using Lumora.Core.Assets.Poiyomi;
using Lumora.Core.Components.Assets;
using Lumora.Core.Math;

namespace Lumora.Core.Components;

// A Poiyomi material laid out the way Poiyomi lays it out: categories, the sections in each with the
// switch that turns the feature on, and the groups a section folds into. Everything starts closed and only
// what is open has rows, so the 2800 properties never all exist as UI at once. A row needs an entry in the
// material to edit; one made just to show a row goes again when the row closes, unless it was changed, so
// browsing leaves the material as it was. Rows that Poiyomi shows only for some settings (the options of one
// lighting mode) follow those settings. -xlinka
[ComponentCategory("Utility/Inspectors")]
public sealed class PoiyomiMaterialInspector : Component, IInspectorActionHandler
{
    private const float LabelWidth = 190f;
    private const float TextureRowHeight = 96f;
    private const float NestIndent = 12f;
    private const double ConditionInterval = 0.25;
    private static readonly string[] Leaves = { "x", "y", "z", "w" };

    public readonly SyncRef<PoiyomiMaterial> Material;
    // Ids of the open categories, sections and groups (PoiyomiNode.Id).
    public readonly SyncFieldList<int> Open;

    private double _nextConditions;
    private bool _watching;
    // Entries this inspector made to show a row (authority only).
    private readonly HashSet<ShaderUniformParam> _shownOnly = new();

    public PoiyomiMaterialInspector()
    {
        Material = new SyncRef<PoiyomiMaterial>(this);
        Open = new SyncFieldList<int>(this);
    }

    public void Setup(PoiyomiMaterial material)
    {
        Material.Target = material;
        foreach (var category in PoiyomiSections.Categories)
            BuildNode(Slot, category, material);
    }

    public override void OnStart()
    {
        base.OnStart();
        if (!_watching)
        {
            _watching = true;
            Open.OnChanged += OnOpenChanged;
        }
    }

    public override void OnDestroy()
    {
        if (_watching)
        {
            _watching = false;
            Open.OnChanged -= OnOpenChanged;
        }
        if (World is { IsDestroyed: false, IsDisposed: false } && Material.Target is { IsDestroyed: false } material)
        {
            foreach (var param in _shownOnly)
                DropIfUntouched(material, param);
        }
        _shownOnly.Clear();
        base.OnDestroy();
    }

    // A press lands on the presser's machine, the list reaches the authority, and the authority builds.
    private void OnOpenChanged(SyncFieldList<int> list) => MarkChangeDirty();

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        var world = World;
        if (world == null || !world.IsAuthority || world.Time.TotalTime < _nextConditions)
            return;
        _nextConditions = world.Time.TotalTime + ConditionInterval;
        if (Material.Target is { IsDestroyed: false } material)
            ApplyConditions(Slot, material);
    }

    public void HandleInspectorAction(string argument)
    {
        if (!int.TryParse(argument, out int id))
            return;
        for (int i = 0; i < Open.Count; i++)
        {
            if (Open[i] == id)
            {
                Open.RemoveAt(i);
                return;
            }
        }
        Open.Add(id);
    }

    public override void OnChanges()
    {
        base.OnChanges();
        if (World?.IsAuthority != true)
            return;
        var material = Material.Target;
        if (material == null || material.IsDestroyed)
            return;
        Reconcile(Slot, material);
    }

    private bool IsOpen(int id)
    {
        foreach (var open in Open)
        {
            if (open == id)
                return true;
        }
        return false;
    }

    // Walks only the bodies that are open: builds the rows of a node opened since, clears one closed since.
    private void Reconcile(Slot container, PoiyomiMaterial material)
    {
        for (int i = 0; i < container.ChildCount; i++)
        {
            var body = container.Children[i];
            if (body.IsDestroyed || !TryBodyId(body, out int id) || PoiyomiSections.Node(id) is not { } node)
                continue;
            bool open = IsOpen(id);
            bool built = HasRows(body);
            if (open && !built)
            {
                foreach (var item in node.Items)
                {
                    if (item is PoiyomiNode child)
                        BuildNode(body, child, material);
                    else if (item is PoiyomiField field)
                        BuildField(body, field, material);
                }
                SetHeaderState(container, id, true);
            }
            else if (!open && built)
            {
                body.DestroyChildren();
                SetHeaderState(container, id, false);
                DropShownOnly(material, node);
            }
            if (open)
                Reconcile(body, material);
        }
    }

    private static bool HasRows(Slot body)
    {
        for (int i = 0; i < body.ChildCount; i++)
        {
            if (!body.Children[i].IsDestroyed)
                return true;
        }
        return false;
    }

    private static bool TryBodyId(Slot slot, out int id) => TryNodeId(slot.SlotName.Value, 'B', out id);

    private static bool TryNodeId(string? name, char prefix, out int id)
    {
        id = -1;
        return name != null && name.Length > 1 && name[0] == prefix && int.TryParse(name.AsSpan(1), out id);
    }

    // Shows and hides the built rows by their Poiyomi conditions; walks only bodies that have rows.
    private static void ApplyConditions(Slot container, PoiyomiMaterial material)
    {
        for (int i = 0; i < container.ChildCount; i++)
        {
            var child = container.Children[i];
            if (child.IsDestroyed)
                continue;
            var name = child.SlotName.Value;
            bool body = false;
            string? condition = null;
            if (TryNodeId(name, 'H', out int id))
            {
                condition = PoiyomiSections.Node(id)?.Condition;
            }
            else if (TryNodeId(name, 'B', out id))
            {
                condition = PoiyomiSections.Node(id)?.Condition;
                body = true;
            }
            else if (name != null && name.StartsWith('_'))
            {
                condition = PoiyomiSections.Field(name)?.Condition;
            }
            if (condition != null)
                Show(child, PoiyomiCondition.Holds(condition, material.GetFloat));
            if (body && child.ActiveSelf.Value && HasRows(child))
                ApplyConditions(child, material);
        }
    }

    private ShaderUniformParam? Entry(PoiyomiMaterial material, string name)
    {
        if (material.Find(name) is { } existing)
            return existing;
        var param = material.Ensure(name);
        if (param != null)
            _shownOnly.Add(param);
        return param;
    }

    // The rows under a closed node are gone: their shown-only entries go too, unless someone changed them or
    // another open row shows the same property.
    private void DropShownOnly(PoiyomiMaterial material, PoiyomiNode closed)
    {
        if (_shownOnly.Count == 0)
            return;
        var names = new List<string>();
        CollectNames(closed, names);
        HashSet<string>? live = null;
        foreach (var name in names)
        {
            if (material.Find(name) is not { } param || !_shownOnly.Contains(param))
                continue;
            if (live == null)
            {
                live = new HashSet<string>();
                CollectLiveRows(Slot, live);
            }
            if (live.Contains(name))
                continue;
            _shownOnly.Remove(param);
            DropIfUntouched(material, param);
        }
    }

    private static void CollectNames(PoiyomiNode node, List<string> names)
    {
        foreach (var item in node.Items)
        {
            if (item is PoiyomiField field)
            {
                names.Add(field.Name);
            }
            else if (item is PoiyomiNode child)
            {
                if (child.Toggle != null)
                    names.Add(child.Toggle);
                CollectNames(child, names);
            }
        }
    }

    // Property names of the rows and switches still built.
    private static void CollectLiveRows(Slot container, HashSet<string> live)
    {
        for (int i = 0; i < container.ChildCount; i++)
        {
            var child = container.Children[i];
            var name = child.SlotName.Value;
            if (child.IsDestroyed || string.IsNullOrEmpty(name))
                continue;
            if (name[0] == '_')
            {
                int mark = name.IndexOf('#');
                live.Add(mark < 0 ? name : name[..mark]);
            }
            else if (TryNodeId(name, 'H', out _) || TryNodeId(name, 'B', out _))
            {
                CollectLiveRows(child, live);
            }
        }
    }

    private static void DropIfUntouched(PoiyomiMaterial material, ShaderUniformParam param)
    {
        if (param.IsDestroyed || param.Value.IsLinked || param.Texture.Target != null || param.Cubemap.Target != null)
            return;
        if (!PoiyomiProperties.TryGet(param.Name.Value, out var property) || param.Value.Value != property.Default)
            return;
        material.Properties.Remove(param);
    }

    private static void Show(Slot row, bool shown)
    {
        if (row.ActiveSelf.Value != shown)
            row.ActiveSelf.Value = shown;
    }

    private static string HeaderText(PoiyomiNode node, bool open) => (open ? "-   " : "+   ") + node.Label;

    private static void SetHeaderState(Slot container, int id, bool open)
    {
        var header = container.FindChild("H" + id);
        var text = header?.GetComponentInChildren<Text>();
        if (text != null && PoiyomiSections.Node(id) is { } node)
            text.Content.Value = HeaderText(node, open);
    }

    // Header row, then the empty body its rows go into once opened.
    private void BuildNode(Slot parent, PoiyomiNode node, PoiyomiMaterial material)
    {
        float height = node.Depth == 0 ? InspectorUI.RowHeight + 4f : node.Depth == 1 ? InspectorUI.RowHeight : InspectorUI.RowHeight - 4f;
        var header = InspectorUI.FixedRow(parent, "H" + node.Id, height, out var ui, Slot);

        ui.PushStyle();
        ui.BackgroundColor(node.Depth == 0 ? InspectorUI.HeaderColor : InspectorUI.RowColor);
        ui.TextColor(node.Depth == 0 ? InspectorUI.CyanColor : node.Depth == 1 ? InspectorUI.TextColor : InspectorUI.MutedColor);
        var button = InspectorUI.RelayButton(ui, this, node.Id.ToString(), HeaderText(node, IsOpen(node.Id)), 0f);
        ui.PopStyle();
        var label = button.Slot.GetComponentInChildren<Text>();
        if (label != null)
        {
            label.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
            label.RectTransform!.OffsetMin.Value = new float2(10f, 0f);
        }

        if (node.Toggle != null && Entry(material, node.Toggle) is { } toggle)
        {
            ui.PushStyle();
            ui.MinWidth(InspectorUI.RowHeight);
            ui.PreferredWidth(InspectorUI.RowHeight);
            ui.FlexibleWidth(0f);
            var area = ui.Next(node.Toggle);
            area.AttachComponent<HorizontalLayout>().ForceExpandHeight.Value = true;
            ui.NestInto(area);
            area.AttachComponent<FloatToggleMemberEditor>().Setup(toggle.Value, "x", ui);
            ui.NestOut();
            ui.PopStyle();
        }

        var body = parent.AddSlot("B" + node.Id);
        body.AttachComponent<RectTransform>();
        var layout = body.AttachComponent<VerticalLayout>();
        layout.Spacing.Value = 2f;
        layout.PaddingLeft.Value = node.Depth == 0 ? 0f : NestIndent;
        layout.ForceExpandWidth.Value = true;
        layout.ForceExpandHeight.Value = false;

        if (node.Condition != null)
        {
            bool shown = PoiyomiCondition.Holds(node.Condition, material.GetFloat);
            Show(header, shown);
            Show(body, shown);
        }
    }

    private void BuildField(Slot parent, PoiyomiField field, PoiyomiMaterial material)
    {
        if (Entry(material, field.Name) is not { } param)
            return;
        float height = field.Kind == PoiyomiFieldKind.Texture ? TextureRowHeight : InspectorUI.RowHeight;
        var row = InspectorUI.FixedRow(parent, field.Key, height, out var ui, Slot);
        if (field.Condition != null)
            Show(row, PoiyomiCondition.Holds(field.Condition, material.GetFloat));

        ui.PushStyle();
        ui.MinWidth(150f);
        ui.PreferredWidth(LabelWidth);
        ui.FlexibleWidth(0f);
        var label = ui.Text(field.Label, InspectorUI.FontSize, InspectorUI.MutedColor);
        InspectorUI.FillParent(label.RectTransform!);
        label.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        label.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        ui.PopStyle();

        IWorldElement edited = field.Kind switch
        {
            PoiyomiFieldKind.Texture => param.Texture,
            PoiyomiFieldKind.Cubemap => param.Cubemap,
            _ => param.Value,
        };
        var tint = row.AttachComponent<MemberStateTint>();
        tint.TargetMember.Target = edited;
        tint.Label.Target = label;
        tint.BaseColor.Value = InspectorUI.MutedColor;

        ui.PushStyle();
        ui.FlexibleWidth(1f);
        var area = ui.Next("Editor");
        var areaLayout = area.AttachComponent<HorizontalLayout>();
        areaLayout.Spacing.Value = 4f;
        areaLayout.ForceExpandHeight.Value = true;
        ui.NestInto(area);
        BuildEditor(area, ui, field, param);
        ui.NestOut();
        ui.PopStyle();
    }

    private static void BuildEditor(Slot area, UIBuilder ui, PoiyomiField field, ShaderUniformParam param)
    {
        switch (field.Kind)
        {
            case PoiyomiFieldKind.Texture:
                area.AttachComponent<TextureRefMemberEditor>().Setup(param.Texture, "", ui);
                return;
            case PoiyomiFieldKind.Cubemap:
                area.AttachComponent<RefMemberEditor>().Setup(param.Cubemap, "", ui);
                return;
            case PoiyomiFieldKind.Toggle:
                area.AttachComponent<FloatToggleMemberEditor>().Setup(param.Value, "x", ui);
                return;
            case PoiyomiFieldKind.Choice:
                var choice = area.AttachComponent<FloatChoiceMemberEditor>();
                choice.Options.Value = field.Options;
                choice.Setup(param.Value, "x", ui);
                return;
            case PoiyomiFieldKind.Range:
                var slider = area.AttachComponent<SliderMemberEditor>();
                slider.Min.Value = field.Min;
                slider.Max.Value = field.Max;
                slider.WholeNumbers.Value = field.Whole;
                slider.Setup(param.Value, "x", ui);
                return;
            case PoiyomiFieldKind.Color:
                area.AttachComponent<ColorMemberEditor>().Setup(param.Value, "", ui);
                return;
            case PoiyomiFieldKind.Vector:
                for (int i = 0; i < field.Components && i < Leaves.Length; i++)
                {
                    ui.PushStyle();
                    ui.FlexibleWidth(1f);
                    var leaf = ui.Next(Leaves[i]);
                    leaf.AttachComponent<HorizontalLayout>();
                    ui.NestInto(leaf);
                    leaf.AttachComponent<PrimitiveMemberEditor>().Setup(param.Value, Leaves[i], ui);
                    ui.NestOut();
                    ui.PopStyle();
                }
                return;
            default:
                area.AttachComponent<PrimitiveMemberEditor>().Setup(param.Value, "x", ui);
                return;
        }
    }
}

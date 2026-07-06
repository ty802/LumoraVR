// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Helio.UI;
using Helio.UI.Layout;
using Lumora.Core;
using Lumora.Core.Components.UI;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Math;

namespace Lumora.Core.Components;

/// <summary>
/// Component browser: walks the ComponentLibrary category tree, attaches the chosen type to the
/// target slot. Rows carry their action in a synced argument (never closures) so presses work for
/// every user. Spawned by the inspector's "Attach Component" button.
/// </summary>
[ComponentCategory("Utility/Inspectors")]
public class ComponentSelectorPanel : Component, IInspectorActionHandler
{
    public readonly SyncRef<Slot> TargetSlot;
    public readonly SyncRef<SceneInspectorPanel> Owner;
    public readonly Sync<string> CategoryPath;

    private readonly SyncRef<Slot> _listContent;
    private readonly Sync<string> _builtPath;

    public ComponentSelectorPanel()
    {
        TargetSlot = new SyncRef<Slot>(this);
        Owner = new SyncRef<SceneInspectorPanel>(this);
        CategoryPath = new Sync<string>(this, "");
        _listContent = new SyncRef<Slot>(this);
        _builtPath = new Sync<string>(this, "unbuilt");
    }

    public static ComponentSelectorPanel Spawn(SceneInspectorPanel owner, Slot target)
    {
        var panelSlot = owner.Slot.Parent?.AddSlot("Component Selector") ?? owner.World.RootSlot.AddSlot("Component Selector");
        panelSlot.Persistent.Value = false;
        panelSlot.Tag.Value = "Developer";
        panelSlot.GlobalPosition = owner.Slot.GlobalPosition + owner.Slot.GlobalRotation * new float3(0.55f, 0f, 0f);
        panelSlot.GlobalRotation = owner.Slot.GlobalRotation;
        panelSlot.LocalScale.Value = owner.Slot.LocalScale.Value;

        var selector = panelSlot.AttachComponent<ComponentSelectorPanel>();
        selector.Owner.Target = owner;
        selector.TargetSlot.Target = target;
        return selector;
    }

    public override void OnAttach()
    {
        base.OnAttach();
        var theme = Slot.GetOrAttachComponent<UITheme>();
        var shell = Slot.GetOrAttachComponent<PanelShell>();
        shell.Title.Value = "Attach Component";
        shell.Size.Value = new float2(620f, 1100f);
        theme.ApplyTo(shell);
        shell.RebuildContent(ui =>
        {
            var scroll = ui.ScrollRect(out var content);
            InspectorUI.FillParent(scroll.Slot.GetComponent<RectTransform>()!);
            content.Slot.AttachComponent<VerticalLayout>();
            _listContent.Target = content.Slot;
        });
    }

    public override void OnChanges()
    {
        base.OnChanges();
        if (World?.IsAuthority != true)
            return;
        if (_builtPath.Value == CategoryPath.Value)
            return;
        _builtPath.Value = CategoryPath.Value;
        RebuildList();
    }

    private void RebuildList()
    {
        var container = _listContent.Target;
        if (container == null || container.IsDestroyed)
            return;
        container.DestroyChildren();

        var node = ComponentLibrary.GetNode(CategoryPath.Value) ?? ComponentLibrary.Root;
        var ui = new UIBuilder(container);
        InspectorUI.ApplyTheme(ui, Slot);
        ui.NestInto(container);
        ui.PushStyle();
        ui.MinHeight(32f);

        if (!string.IsNullOrEmpty(CategoryPath.Value))
            AddRow(container, "< Back", "back:", InspectorUI.MutedColor);

        foreach (var sub in node.Subcategories.Values)
            AddRow(container, sub.Name + " >", "cat:" + sub.Path, InspectorUI.AccentColor);

        foreach (var type in node.Types)
            AddRow(container, type.Name, "type:" + type.AssemblyQualifiedName, InspectorUI.TextColor);

        ui.PopStyle();
        ui.NestOut();
    }

    private void AddRow(Slot container, string label, string action, color tint)
    {
        InspectorUI.FixedRow(container, label, 32f, out var ui, Slot);
        ui.PushStyle();
        ui.TextColor(tint);
        InspectorUI.RelayButton(ui, this, action, label, 0f);
        ui.PopStyle();
    }

    /// <summary>Handle a browser row action ("back:", "cat:Path", "type:AssemblyQualifiedName").</summary>
    public void HandleInspectorAction(string action)
    {
        if (action.StartsWith("back:", StringComparison.Ordinal))
        {
            var path = CategoryPath.Value ?? "";
            int slash = path.LastIndexOf('/');
            CategoryPath.Value = slash > 0 ? path[..slash] : "";
            return;
        }
        if (action.StartsWith("cat:", StringComparison.Ordinal))
        {
            CategoryPath.Value = action[4..];
            return;
        }
        if (action.StartsWith("type:", StringComparison.Ordinal))
        {
            var target = TargetSlot.Target;
            var type = Type.GetType(action[5..]);
            if (target != null && type != null && typeof(Component).IsAssignableFrom(type))
            {
                target.AttachComponent(type);
                Owner.Target?.MarkListsDirty();
            }
            Slot.Destroy();
        }
    }
}

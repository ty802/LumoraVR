// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Reflection;
using Helio.UI;
using Helio.UI.Layout;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Components.Assets;
using Lumora.Core.Components.UI;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Math;

namespace Lumora.Core.Components;

// focused inspector for ONE material provider: header with the material type and its slot, then
// the material's sync members through the regular member-editor machinery. custom shader materials
// additionally render their uniform Parameters as first-class rows: ranged floats get sliders
// bound to the param value with the parsed range, color vec4s get channel fields plus a live
// swatch, textures get the drop-assignable texture editor. the sandbox diagnostics block appends
// at the bottom.
[ComponentCategory("Utility/Inspectors")]
public class MaterialInspectorPanel : Component, IInspectorActionHandler
{
    public readonly SyncRef<MaterialProvider> Target;

    // Shader source starts folded: a material reads as clean typed fields first; the code box is an
    // opt-in expansion at the bottom.
    public readonly Sync<bool> SourceExpanded;

    private readonly SyncRef<Slot> _content;
    private readonly SyncRef<Text> _title;

    private bool _rowsDirty;
    private bool _hadShaderSource;
    private CustomShaderMaterial? _hookedMaterial;

    public MaterialInspectorPanel()
    {
        Target = new SyncRef<MaterialProvider>(this);
        SourceExpanded = new Sync<bool>(this, false);
        _content = new SyncRef<Slot>(this);
        _title = new SyncRef<Text>(this);
    }

    // action strings: fold/unfold the shader source, destroy the inspected material (undoable)
    public void HandleInspectorAction(string argument)
    {
        if (argument == "togglesource")
        {
            // Rebuild the rows: the code editor must be BUILT while its slot is ACTIVE or its text
            // never lays out / shapes (building UI under an inactive slot skips the layout pass, so
            // an active-flip toggle showed an empty, unscrollable box). A rebuild on a user click is
            // cheap - it's not a per-frame path.
            SourceExpanded.Value = !SourceExpanded.Value;
            _rowsDirty = true;
            MarkChangeDirty();
            return;
        }
        if (argument == "destroymat" && Target.Target is { IsDestroyed: false } material)
        {
            ComponentUndo.RecordDestroy(this, material);
            // The husk-close poll in OnUpdate takes the panel down next frame.
        }
    }

    public static MaterialInspectorPanel Spawn(World world, MaterialProvider material, float3 position, floatQ rotation)
    {
        var panelSlot = world.RootSlot.AddSlot("Material Inspector");
        panelSlot.Persistent.Value = false;
        panelSlot.Tag.Value = "Developer";
        panelSlot.GlobalPosition = position;

        // Face the local user's head from wherever we land (readable +Z front toward the viewer), so the
        // panel is never spawned showing its back. Falls back to the caller's rotation with no head. -xlinka
        var head = world.LocalUser?.Root?.HeadSlot;
        var toViewer = head != null ? head.GlobalPosition - position : float3.Zero;
        toViewer.y = 0f;
        panelSlot.GlobalRotation = toViewer.LengthSquared > 1e-6f
            ? floatQ.AxisAngleRad(float3.Up, MathF.Atan2(toViewer.x, toViewer.z))
            : rotation;
        panelSlot.LocalScale.Value = float3.One * 0.0005f;

        var panel = panelSlot.AttachComponent<MaterialInspectorPanel>();
        panel.Target.Target = material;
        return panel;
    }

    public override void OnAttach()
    {
        base.OnAttach();

        var theme = Slot.GetOrAttachComponent<UITheme>();
        // Same dark set as the scene inspector so the panels read as one tool family. -xlinka
        theme.PanelBackground.Value = new color(0.075f, 0.07f, 0.115f, 1f);
        theme.Header.Value = new color(0.11f, 0.10f, 0.17f, 1f);
        theme.ButtonFill.Value = new color(0.22f, 0.20f, 0.34f, 1f);
        theme.Accent.Value = InspectorUI.AccentColor;
        theme.Separator.Value = new color(0.52f, 0.46f, 0.82f, 0.6f);
        theme.Border.Value = new color(0.52f, 0.46f, 0.82f, 0.45f);

        var shell = Slot.GetOrAttachComponent<PanelShell>();
        shell.Title.Value = "Material";
        // Big frame: params sit up top (right under the properties) so they're reachable even if the shader box at
        // the bottom runs past the view, and the panel scrolls for the rest. -xlinka
        shell.Size.Value = new float2(920f, 1120f);
        theme.ApplyTo(shell);

        shell.RebuildContent(BuildLayout);
        _rowsDirty = true;
    }

    private void BuildLayout(UIBuilder ui)
    {
        var page = ui.Current;
        InspectorUI.ApplyTheme(ui, Slot);

        var vLayout = page.AttachComponent<VerticalLayout>();
        vLayout.Spacing.Value = 6f;
        vLayout.PaddingLeft.Value = 8f;
        vLayout.PaddingRight.Value = 8f;
        vLayout.PaddingTop.Value = 8f;
        vLayout.PaddingBottom.Value = 8f;
        vLayout.ForceExpandWidth.Value = true;
        vLayout.ForceExpandHeight.Value = false;

        InspectorUI.FixedRow(page, "Header", 40f, out var headerUi, Slot);
        headerUi.PushStyle();
        headerUi.FlexibleWidth(1f);
        _title.Target = headerUi.Text("", InspectorUI.FontSize + 2f, InspectorUI.TextColor);
        InspectorUI.FillParent(_title.Target.RectTransform!);
        _title.Target.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        _title.Target.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        // The slot half of the title is user content with inline style tags.
        _title.Target.RichText.Value = true;
        headerUi.PopStyle();

        // Destroy the inspected material (undoable serialize-then-destroy); the panel closes itself.
        headerUi.PushStyle();
        headerUi.TextColor(InspectorUI.DangerColor);
        InspectorUI.RelayButton(headerUi, this, "destroymat", "Destroy", 74f);
        headerUi.PopStyle();

        // Scroll list fills the rest; rows stack from the top at fixed heights.
        var host = page.AddSlot("Rows");
        host.AttachComponent<RectTransform>();
        var hostLE = host.AttachComponent<LayoutElement>();
        hostLE.FlexibleHeight.Value = 1f;
        hostLE.MinHeight.Value = 300f;

        var scrollUi = new UIBuilder(host);
        InspectorUI.ApplyTheme(scrollUi, Slot);
        var scroll = scrollUi.ScrollRect(out var content, null, InspectorUI.PaneColor);
        InspectorUI.FillParent(scroll.Slot.GetComponent<RectTransform>()!);

        var contentLayout = content.Slot.AttachComponent<VerticalLayout>();
        contentLayout.Spacing.Value = 3f;
        contentLayout.ForceExpandWidth.Value = true;
        contentLayout.ForceExpandHeight.Value = false;
        _content.Target = content.Slot;
    }

    public override void OnUpdate(float delta)
    {
        if (World?.IsAuthority != true)
            return;

        // Close the husk when the inspected material dies - a panel pointing at nothing is just clutter.
        // RawTarget: a destroyed ref's Target already reads null, which must NOT close a fresh panel.
        if (Target.RawTarget is { IsDestroyed: true })
        {
            Slot.Destroy();
            return;
        }

        // A shader asset that finishes loading AFTER the first render never touches the Parameters
        // list, so nothing else re-renders the rows - diagnostics would stay stuck on "no shader
        // source" forever without this edge watch.
        if (Target.Target is CustomShaderMaterial custom)
        {
            bool hasSource = !string.IsNullOrWhiteSpace(custom.Shader.Asset?.Source);
            if (hasSource != _hadShaderSource)
            {
                _rowsDirty = true;
                MarkChangeDirty();
            }
        }

        NudgeUnregisteredEditor();
    }

    // The scroll content freezes its child list once clean (its ScrollRect-viewport parent runs no
    // LayoutController, so ComputeRects cache-skips it), and the late-built code editor host misses
    // the one post-rebuild enumeration - staying unregistered/0-tall. Self-heal: whenever the source
    // is expanded and the host isn't laid out, force the content to re-enumerate. Once the host has a
    // real rect this is just a cheap height check per frame (no dirtying, no re-tessellation), so it
    // both guarantees the editor appears and stops churning the instant it does - no frame cap to
    // race against. -xlinka
    private void NudgeUnregisteredEditor()
    {
        if (!SourceExpanded.Value)
            return;

        var content = _content.Target;
        var host = content?.FindChild("CodeEditor", recursive: false);
        var hostRt = host?.GetComponent<RectTransform>();
        if (hostRt == null)
            return;
        if (hostRt.LocalComputeRect.height > 1f)
            return; // laid out; nothing to do

        var contentRt = content!.GetComponent<RectTransform>();
        contentRt?.MarkChangeDirty();   // content self-dirty: the cached-layout pass can't skip it
        contentRt?.Canvas?.MarkDirty(); // full pass: re-measure + re-register every child
    }

    public override void OnChanges()
    {
        base.OnChanges();
        if (World?.IsAuthority != true)
            return;

        if (Target.GetWasChangedAndClear())
            _rowsDirty = true;

        if (!_rowsDirty)
            return;
        _rowsDirty = false;

        HookParameterEvents();
        RebuildRows();
    }

    public override void OnDestroy()
    {
        UnhookParameterEvents();
        base.OnDestroy();
    }

    // Param list changes (shader reparse, remote edit) re-render the rows; flag-and-defer like the
    // collection editor so nothing rebuilds mid-mutation.
    private void HookParameterEvents()
    {
        var material = Target.Target as CustomShaderMaterial;
        if (ReferenceEquals(material, _hookedMaterial))
            return;
        UnhookParameterEvents();
        _hookedMaterial = material;
        if (material != null)
        {
            ((ISyncList)material.Parameters).ElementsAdded += OnParamsChanged;
            ((ISyncList)material.Parameters).ElementsRemoved += OnParamsChanged;
        }
    }

    private void UnhookParameterEvents()
    {
        if (_hookedMaterial != null)
        {
            ((ISyncList)_hookedMaterial.Parameters).ElementsAdded -= OnParamsChanged;
            ((ISyncList)_hookedMaterial.Parameters).ElementsRemoved -= OnParamsChanged;
            _hookedMaterial = null;
        }
    }

    private void OnParamsChanged(ISyncList list, int index, int count)
    {
        _rowsDirty = true;
        MarkChangeDirty();
    }

    private void RebuildRows()
    {
        var content = _content.Target;
        if (content == null || content.IsDestroyed)
            return;

        var material = Target.Target;
        var title = _title.Target;
        if (title != null && !title.IsDestroyed)
            title.Content.Value = material == null
                ? "<none>"
                : $"{material.GetType().Name} on {material.Slot?.SlotName.Value}";

        content.DestroyChildren();
        if (material == null || material.IsDestroyed)
            return;

        // Gripping the header pulls the material's card (wired here, not BuildLayout: Target is only
        // set after the shell builds).
        if (title != null && !title.IsDestroyed)
        {
            var headerSource = title.Slot.GetComponent<ReferenceProxySource>()
                ?? title.Slot.AttachComponent<ReferenceProxySource>();
            headerSource.Target.Target = material;
        }

        var custom = material as CustomShaderMaterial;
        _hadShaderSource = custom != null && !string.IsNullOrWhiteSpace(custom.Shader.Asset?.Source);

        // Regular member rows, minus the raw Parameters list (it gets first-class rows below).
        // [Group] fields open sections here too, same semantics as the generic builder.
        string? currentGroup = null;
        for (int i = 0; i < material.SyncMemberCount; i++)
        {
            var fieldInfo = material.GetSyncMemberFieldInfo(i);
            if (fieldInfo?.GetCustomAttribute<HideInInspectorAttribute>() != null)
                continue;
            var member = material.GetSyncMember(i);
            if (member == null)
                continue;
            if (custom != null && ReferenceEquals(member, custom.Parameters))
                continue;
            var group = fieldInfo?.GetCustomAttribute<GroupAttribute>();
            if (group != null && !string.Equals(group.Name, currentGroup, StringComparison.Ordinal))
            {
                InspectorUI.SectionHeader(content, group.Name.ToUpperInvariant(), Slot);
                currentGroup = group.Name;
            }
            SyncMemberEditorBuilder.Build(member, material.GetSyncMemberName(i), fieldInfo, content, Slot);
        }

        // Uniform params FIRST (right under the property rows) so they're reachable without scrolling
        // past the whole shader source - that ordering was the actual "can't get to the params" complaint. -xlinka
        if (custom != null)
        {
            InspectorUI.SectionHeader(content, "UNIFORMS", Slot);
            int paramCount = 0;
            foreach (var param in custom.Parameters)
            {
                BuildParamRow(content, param);
                paramCount++;
            }
            if (paramCount == 0)
                BuildCenteredLabel(content, "no uniforms");
        }

        WorkerInspectorBuilder.BuildMethodRows(material, content, Slot);

        // Diagnostics block (the sandbox verdict for custom shaders) appends after everything.
        if (material is ICustomInspectorUI extra)
        {
            InspectorUI.SectionHeader(content, "DIAGNOSTICS", Slot);
            var bodyHost = content.AddSlot("CustomBody");
            bodyHost.AttachComponent<RectTransform>();
            var bodyLayout = bodyHost.AttachComponent<VerticalLayout>();
            bodyLayout.Spacing.Value = 2f;
            bodyLayout.ForceExpandWidth.Value = true;
            bodyLayout.ForceExpandHeight.Value = false;
            var bodyUi = new UIBuilder(bodyHost);
            InspectorUI.ApplyTheme(bodyUi, Slot);
            try { extra.BuildInspectorBody(bodyUi); }
            catch { /* a broken body must not take the panel down; leave the host empty */ }
        }

        // Shader source LAST and FOLDED by default: the material reads as fields first, the code box is
        // an opt-in expansion so it stops dominating the panel. -xlinka
        if (custom != null)
        {
            InspectorUI.SectionHeader(content, "SHADER SOURCE", Slot);
            InspectorUI.FixedRow(content, "SourceToggle", InspectorUI.RowHeight, out var toggleUi, Slot);
            toggleUi.PushStyle();
            toggleUi.TextColor(InspectorUI.AccentColor);
            InspectorUI.RelayButton(toggleUi, this, "togglesource",
                SourceExpanded.Value ? "v Hide Source" : "> Edit Source", 0f);
            toggleUi.PopStyle();
            // Built ONLY when expanded and always ACTIVE - the editor's text needs a live layout
            // pass to shape, which an inactive slot never gets. Collapse rebuilds without it.
            if (SourceExpanded.Value)
                BuildCodeEditor(content, custom);
        }

        // NudgeUnregisteredEditor (OnUpdate) self-heals the late-built editor host's registration while
        // the source is expanded - no arming needed. -xlinka
    }

    private void BuildCenteredLabel(Slot content, string label)
    {
        InspectorUI.FixedRow(content, "Label", InspectorUI.RowHeight, out var ui, Slot);
        ui.PushStyle();
        ui.FlexibleWidth(1f);
        var text = ui.Text(label, InspectorUI.FontSize, InspectorUI.TextColor);
        InspectorUI.FillParent(text.RectTransform!);
        text.HorizontalAlignment.Value = TextHorizontalAlignment.Center;
        text.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        ui.PopStyle();
    }

    // One uniform row: chip + fixed-width name, then a type-appropriate editor filling the rest.
    private void BuildParamRow(Slot content, ShaderUniformParam param)
    {
        string name = string.IsNullOrEmpty(param.Name.Value) ? "uniform" : param.Name.Value;
        bool isTexture = param.Type.Value == ShaderUniformType.Texture2D;
        // Texture uniforms carry the full 96px preview block; everything else stays a compact row.
        var row = InspectorUI.FixedRow(content, name, isTexture ? 96f : InspectorUI.RowHeight, out var ui, Slot);

        var source = row.AttachComponent<ReferenceProxySource>();
        source.Target.Target = param;
        InspectorUI.MemberChip(ui, isTexture ? InspectorUI.AccentColor : InspectorUI.AxisZColor, source);

        ui.PushStyle();
        ui.MinWidth(150f);
        ui.PreferredWidth(190f);
        ui.FlexibleWidth(0f);
        var label = ui.Text($"{name}:", InspectorUI.FontSize, InspectorUI.MutedColor);
        InspectorUI.FillParent(label.RectTransform!);
        label.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        label.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        ui.PopStyle();

        ui.PushStyle();
        ui.FlexibleWidth(1f);
        var editorArea = ui.Next("Editor");
        var areaLayout = editorArea.AttachComponent<HorizontalLayout>();
        areaLayout.Spacing.Value = 4f;
        areaLayout.ForceExpandHeight.Value = true;
        ui.NestInto(editorArea);
        ShaderUniformParamEditor.BuildValueEditor(editorArea, ui, param);
        ui.NestOut();
        ui.PopStyle();
    }

    // The scrollable gdshader code editor, loaded with the material's current source. Editing writes back
    // through a new local asset on Apply, so it re-runs the sandbox gate and reparses the params below. -xlinka
    private void BuildCodeEditor(Slot content, CustomShaderMaterial custom)
    {
        var host = content.AddSlot("CodeEditor");
        host.AttachComponent<RectTransform>();
        host.AttachComponent<LayoutElement>();
        var editor = host.AttachComponent<Lumora.Core.Components.UI.CodeEditor>();
        // autoGrow: this panel is a scrolling inspector, so the editor sizes itself to fit ALL its lines and
        // builds no inner ScrollRect - the OUTER panel scrolls through it as one. Standalone mode would put a
        // scroll chunk inside a scroll chunk, and the inner viewport's clip window can't ride the outer scroll
        // (a material rect is a fixed canvas-space window), so the code got sliced away as the panel moved. -xlinka
        editor.Setup(custom, Slot, autoGrow: true);
    }
}

// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Helio.UI;
using Lumora.Core.Components.Assets;
using Lumora.Core.Components.Import;
using Lumora.Core.Components.Meshes;
using Lumora.Core.Components.UI;
using Lumora.Core.Math;
using Lumora.Core.Scripting;
using Lumora.Script;
using Mask = Helio.UI.Mask;

namespace Lumora.Core.Components.Scripting;

// One node's card: a small canvas on a local slot under the node, built on each machine from the
// node's data and thrown away and rebuilt when that data or its problems change. Nothing on it syncs;
// what a hand does on it (typing a constant, dropping a reference, cycling the overload) writes the
// node, and the node is what everyone shares.
//
// A node that does not compile wears a red outline and header and says why along its bottom edge. One the
// running script lately faulted in wears an orange outline and says what went wrong there. -xlinka
public sealed class ScriptNodeCard
{
    private float EditorLeft => Layout.EditorLeft;
    private float EditorWidth => Layout.EditorWidth;
    private const float BackingDepth = 4f;
    private const float BackingMargin = 6f;

    private int _editing;
    private Slot? _backing;
    private Slot? _options;
    private readonly List<ScriptCardHandle> _handles = new();
    private Text? _readout;
    private ScriptGraphNode? _shows;

    public Slot Root { get; }
    public ScriptCardLayout Layout { get; }
    public int Stamp { get; }

    // A card being typed into is left alone until the typing ends, or the rebuild would eat the field.
    public bool IsEditing => _editing > 0;

    // The handle on this card for one of its ports, or its header.
    public ScriptCardHandle? FindHandle(ScriptCardHandleKind kind, string port)
    {
        foreach (var handle in _handles)
        {
            if (!handle.IsDestroyed && handle.Kind == kind && (kind == ScriptCardHandleKind.Header || handle.Port == port))
                return handle;
        }
        return null;
    }

    private ScriptNodeCard(Slot root, ScriptCardLayout layout, int stamp)
    {
        Root = root;
        Layout = layout;
        Stamp = stamp;
    }

    public void Destroy()
    {
        if (!Root.IsDestroyed)
            Root.Destroy();
        if (_backing is { IsDestroyed: false } backing)
            backing.Destroy();
    }

    public static ScriptNodeCard Build(ScriptGraphNode node, IReadOnlyList<string> problems, int stamp, string? fault = null, bool optionsOpen = false,
        IReadOnlyList<string>? notes = null)
    {
        var world = node.World!;
        var overload = node.Resolve(out var resolveError);
        var shown = new List<string>(problems);
        if (resolveError != null)
            shown.Insert(0, resolveError);
        bool bad = shown.Count > 0;
        bool faulted = !string.IsNullOrEmpty(fault);
        var noted = notes ?? Array.Empty<string>();
        var layout = ScriptCardLayout.For(node, shown.Count + noted.Count + (faulted ? 1 : 0));
        layout.OptionsOpen = optionsOpen && layout.OptionsRows > 0;
        float width = layout.Width;
        float height = layout.Height;

        var root = node.Slot.AddLocalSlot("Card");
        root.LocalPosition.Value = float3.Zero;
        root.LocalRotation.Value = floatQ.Identity;
        root.LocalScale.Value = new float3(ScriptEditorStyle.CardScale, ScriptEditorStyle.CardScale, ScriptEditorStyle.CardScale);
        var rect = root.AttachComponent<RectTransform>();
        rect.AnchorMin.Value = rect.AnchorMax.Value = new float2(0.5f, 0.5f);
        rect.OffsetMin.Value = new float2(0f, -height);
        rect.OffsetMax.Value = new float2(width, 0f);
        root.AttachComponent<Canvas>();
        var font = root.AddSlot("Font").AttachComponent<FontProvider>();
        font.URL.Value = ImportDialog.ResolveFontUrl(world) ?? DashTheme.FontRegular;
        font.FallbackURLs.Add(DashTheme.FontRegular);

        var content = root.AddSlot("Content");
        Fill(content);
        content.AttachComponent<GraphicChunkRoot>();
        var background = content.AttachComponent<RoundedPanel>();
        background.Color.Value = ScriptEditorStyle.CardBackground;
        background.CornerRadius.Value = 12f;
        background.OutlineColor.Value = bad ? ScriptEditorStyle.Problem : faulted ? ScriptEditorStyle.Fault : ScriptEditorStyle.CardOutline;
        background.OutlineThickness.Value = bad || faulted ? 4f : 2f;

        var card = new ScriptNodeCard(root, layout, stamp);
        card._backing = Backing(node, width, height);
        card.BuildHeader(content, font, node, bad);
        card.BuildPorts(content, font, node);
        card.BuildSettings(content, font, node);
        card.BuildExtra(content, font, node);
        card.BuildPreviewWell(content);
        card.BuildProblems(content, font, shown, noted, faulted ? "at run time: " + fault : null);
        card.BuildOptions(content, font, node, overload);
        return card;
    }

    // A frosted slab just behind the card, the backing a panel has, so a card is still something from
    // behind or edge on. It sits beside the canvas rather than in it, so the card's layout and batching
    // never take it over.
    private static Slot Backing(ScriptGraphNode node, float width, float height)
    {
        float scale = ScriptEditorStyle.CardScale;
        var slot = node.Slot.AddLocalSlot("Card Backing");
        slot.LocalPosition.Value = new float3(width * 0.5f * scale, -height * 0.5f * scale, -BackingDepth * scale);
        slot.LocalScale.Value = new float3(scale, scale, scale);
        var slab = slot.AttachComponent<RoundedQuadMesh>();
        slab.CornerSegments.Value = 8;
        slab.DualSided.Value = false;
        slab.Size.Value = new float2(width + BackingMargin * 2f, height + BackingMargin * 2f);
        slab.CornerRadius.Value = 12f + BackingMargin;
        var renderer = slot.AttachComponent<MeshRenderer>();
        renderer.Mesh.Target = slab;
        var glass = DashGlass.Attach(slot);
        // Drawn before the card's canvas, like a panel's glass before its backdrop.
        glass.RenderQueue.Value = 2872;
        renderer.Material.Target = glass;
        renderer.ShadowCastMode.Value = ShadowCastMode.Off;
        return slot;
    }

    private void BuildHeader(Slot content, FontProvider font, ScriptGraphNode node, bool bad)
    {
        float width = Layout.Width;
        var header = content.AddSlot("Header");
        Place(header, 0f, 0f, width, ScriptEditorStyle.HeaderHeight);
        var image = header.AttachComponent<Image>();
        image.Tint.Value = bad ? ScriptEditorStyle.Problem : DashTheme.Surface;
        image.CornerRadius.Value = 12f;
        header.AttachComponent<Button>();
        var handle = header.AttachComponent<ScriptCardHandle>();
        handle.Node = node;
        handle.Kind = ScriptCardHandleKind.Header;
        _handles.Add(handle);

        // A quiet category accent leaves the title itself clear.
        var accent = content.AddSlot("Category");
        Place(accent, 12f, ScriptEditorStyle.HeaderHeight - 2f, width - 24f, 2f);
        accent.AttachComponent<Image>().Tint.Value = ScriptEditorStyle.Category(node.Category);

        Label(header, font, node.Title, 14f, 0f, width - (Layout.OptionsRows > 0 ? 62f : 28f),
            ScriptEditorStyle.HeaderHeight, DashTheme.Text, size: 17f);
        if (Layout.OptionsRows > 0)
        {
            // Sibling of the drag header, so the node tool does not mistake this button for a grab.
            var options = Builder(content, font).Button("···", (_, _) => SetOptionsOpen(!Layout.OptionsOpen), DashTheme.Field);
            Place(options.Slot, width - 40f, 6f, 28f, 28f);
        }
    }

    public void SetOptionsOpen(bool open)
    {
        Layout.OptionsOpen = open && Layout.OptionsRows > 0;
        if (_options != null)
            _options.ActiveSelf.Value = Layout.OptionsOpen;
        var rect = Root.GetComponent<RectTransform>()!;
        rect.OffsetMin.Value = new float2(0f, -Layout.Height);
        if (_backing is { IsDestroyed: false })
        {
            _backing.LocalPosition.Value = new float3(Layout.Width * 0.5f, -Layout.Height * 0.5f, -BackingDepth) * ScriptEditorStyle.CardScale;
            _backing.GetComponent<RoundedQuadMesh>()!.Size.Value = new float2(Layout.Width + BackingMargin * 2f, Layout.Height + BackingMargin * 2f);
        }
    }

    private void BuildOptions(Slot content, FontProvider font, ScriptGraphNode node, NodeOverload? overload)
    {
        if (Layout.OptionsRows == 0)
            return;
        _options = content.AddSlot("Options");
        Place(_options, 12f, Layout.OptionsTop, Layout.Width - 24f, Layout.OptionsRows * ScriptEditorStyle.RowHeight);
        _options.ActiveSelf.Value = Layout.OptionsOpen;
        float width = Layout.Width - 24f;
        var ui = Builder(_options, font);
        var choices = ScriptGraphEditing.SignatureChoices(node);
        var signature = overload?.Signature ?? node.Signature.Value ?? string.Empty;
        bool typeRow = overload?.Definition.IsGeneric == true || choices.Count > 1 || overload == null;
        if (typeRow)
        {
            Label(_options, font, "Type", 2f, 0f, 44f, ScriptEditorStyle.RowHeight, DashTheme.TextMuted);
            float fieldWidth = width - 52f - (choices.Count > 1 ? 68f : 0f);
            if (overload?.Definition.IsGeneric == true || overload == null)
                TypeEditor(_options, font, node, signature, 50f, fieldWidth);
            else
                Label(_options, font, signature, 50f, 0f, fieldWidth, ScriptEditorStyle.RowHeight, DashTheme.TextDim);
            if (choices.Count > 1)
            {
                var previous = ui.Button("‹", (_, _) => ScriptGraphEditing.CycleOverload(node, -1), DashTheme.Field);
                Place(previous.Slot, width - 62f, 2f, 28f, 28f);
                var next = ui.Button("›", (_, _) => ScriptGraphEditing.CycleOverload(node, 1), DashTheme.Field);
                Place(next.Slot, width - 28f, 2f, 28f, 28f);
            }
        }
        if (overload?.Definition.IsVariadic == true)
        {
            float top = typeRow ? ScriptEditorStyle.RowHeight : 0f;
            Label(_options, font, "Inputs", 2f, top, width - 76f, ScriptEditorStyle.RowHeight, DashTheme.TextMuted);
            var fewer = ui.Button("−", (_, _) => ScriptGraphEditing.ChangeInputCount(node, -1), DashTheme.Field);
            Place(fewer.Slot, width - 62f, top + 2f, 28f, 28f);
            var more = ui.Button("+", (_, _) => ScriptGraphEditing.ChangeInputCount(node, 1), DashTheme.Field);
            Place(more.Slot, width - 28f, top + 2f, 28f, 28f);
        }
    }

    // The options drawer's type field. It commits when it lets go of focus; text the node cannot take goes back to
    // what the node has.
    private void TypeEditor(Slot header, FontProvider font, ScriptGraphNode node, string signature, float x, float width)
    {
        var ui = Builder(header, font);
        var input = ui.TextInput(signature, "type", null, false, DashTheme.Field);
        Place(input.Slot, x, 2f, width, ScriptEditorStyle.RowHeight - 4f);
        ClipField(input);
        input.EditingStarted += _ => _editing++;
        input.FocusLost += (field, text) =>
        {
            _editing = System.Math.Max(0, _editing - 1);
            if (!ScriptGraphEditing.TrySetSignature(node, text, out _) && !node.IsDestroyed && !input.IsDestroyed)
                input.Text.Value = node.Overload?.Signature ?? node.Signature.Value ?? string.Empty;
        };
    }

    private void BuildPorts(Slot content, FontProvider font, ScriptGraphNode node)
    {
        float width = Layout.Width;
        float row = ScriptEditorStyle.RowHeight;
        for (int i = 0; i < Layout.Left.Count; i++)
        {
            var port = Layout.Left[i];
            float top = Layout.RowTop(i);
            bool hasEditor = port.Kind == ScriptCardHandleKind.DataInput && port.Type != null && node.FindInput(port.Name)?.Node.Target == null;
            Handle(content, node, port, -ScriptEditorStyle.HandleSize * 0.5f, top, -10f, EditorLeft + 6f);
            Label(content, font, port.Kind == ScriptCardHandleKind.FlowInput ? "›" : port.Name,
                ScriptEditorStyle.PortInset, top, Layout.InputLabelWidth, row, DashTheme.TextDim);

            if (hasEditor)
                ValueEditor(content, font, node, port.Name, port.Type!, EditorLeft, top);
        }

        for (int i = 0; i < Layout.Right.Count; i++)
        {
            var port = Layout.Right[i];
            float top = Layout.RowTop(i);
            float left = Layout.OutputLeft - 8f;
            Handle(content, node, port, width - ScriptEditorStyle.HandleSize * 0.5f, top, left, width - left + 10f);
            Label(content, font, port.Name, Layout.OutputLeft, top, Layout.OutputLabelWidth, row,
                DashTheme.TextDim, TextHorizontalAlignment.Right);
        }
    }

    private void BuildSettings(Slot content, FontProvider font, ScriptGraphNode node)
    {
        for (int i = 0; i < Layout.Settings.Count; i++)
        {
            var setting = Layout.Settings[i];
            float top = Layout.SettingTop(i);
            if (setting.Kind == ScriptCardHandleKind.Variable)
            {
                Handle(content, node, setting, 4f, top, 0f, Layout.Width);
                var target = node.FindValue(setting.Name)?.Reference.Target as ScriptGraphNode;
                string writes = target == null ? "drag onto a Stored Value" : "writes " + VariableName(target);
                Label(content, font, writes, 26f, top, Layout.Width - 36f, ScriptEditorStyle.RowHeight,
                    target == null ? DashTheme.Warning : DashTheme.TextDim);
                continue;
            }

            Label(content, font, setting.Name, ScriptEditorStyle.PortInset, top, Layout.InputLabelWidth,
                ScriptEditorStyle.RowHeight, DashTheme.TextDim);
            if (setting.Type != null)
                ValueEditor(content, font, node, setting.Name, setting.Type, EditorLeft, top);
        }
    }

    // A Display's or Pulse's readout, kept current from what the host shows on the node; a Call's button.
    private void BuildExtra(Slot content, FontProvider font, ScriptGraphNode node)
    {
        if (!Layout.Readout && !Layout.CallButton)
            return;
        float top = Layout.ExtraTop + 4f;
        float height = Layout.ExtraHeight - 8f;
        float width = Layout.Width - 24f;
        if (Layout.CallButton)
        {
            var call = Builder(content, font).Button("Call", (_, _) => ScriptHost.Call(node), DashTheme.Accent);
            Place(call.Slot, 10f, top, width, height);
            return;
        }
        var well = content.AddSlot("Readout");
        Place(well, 10f, top, width, height);
        var image = well.AttachComponent<Image>();
        image.Tint.Value = DashTheme.Field;
        image.CornerRadius.Value = 6f;
        var text = Builder(well, font).Text(ReadoutText(node), ScriptEditorStyle.FontSize + 2f, DashTheme.Text);
        Fill(text.Slot);
        well.AttachComponent<Mask>().ShowMaskGraphic.Value = true;
        text.AutoSizeHorizontal.Value = text.AutoSizeVertical.Value = true;
        text.AutoSizeMin.Value = 12f;
        text.HorizontalAlignment.Value = TextHorizontalAlignment.Center;
        text.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        _readout = text;
        _shows = node;
    }

    // Brings the readout up to what the host last showed, without rebuilding the card.
    public void Refresh()
    {
        if (_readout == null || _readout.IsDestroyed || _shows == null || _shows.IsDestroyed)
            return;
        var text = ReadoutText(_shows);
        if (_readout.Content.Value != text)
            _readout.Content.Value = text;
    }

    private static string ReadoutText(ScriptGraphNode node)
        => string.IsNullOrEmpty(node.Shown.Value) ? "..." : node.Shown.Value;

    // The dark square a shader node's preview sits in front of; the preview itself is the view's, not the card's.
    private void BuildPreviewWell(Slot content)
    {
        if (!Layout.Preview)
            return;
        var well = content.AddSlot("Preview");
        Place(well, 10f, Layout.PreviewTop, Layout.Width - 20f, ScriptCardLayout.PreviewSize);
        var image = well.AttachComponent<Image>();
        image.Tint.Value = DashTheme.Field;
        image.CornerRadius.Value = 6f;
    }

    // What is wrong in red, then notes in the warning colour, then a run-time fault.
    private void BuildProblems(Slot content, FontProvider font, List<string> problems, IReadOnlyList<string> notes, string? fault)
    {
        var lines = new List<(string Text, color Tint)>();
        foreach (var problem in problems)
            lines.Add((problem, ScriptEditorStyle.Problem));
        foreach (var note in notes)
            lines.Add((note, DashTheme.Warning));
        if (fault != null)
            lines.Add((fault, ScriptEditorStyle.Fault));
        for (int i = 0; i < Layout.ProblemLines && i < lines.Count; i++)
        {
            Label(content, font, Shorten(lines[i].Text, 64), 12f, Layout.ProblemTop(i),
                Layout.Width - 24f, ScriptEditorStyle.RowHeight, lines[i].Tint, size: 12f);
        }
    }

    // A port answers across its side of the row, label and all, not only on its dot: a beam at arm's
    // length has to be able to take hold of it and land on it. The dot lights up while the beam is on it.
    private void Handle(Slot content, ScriptGraphNode node, in ScriptCardPort port, float dotX, float top, float left, float width)
    {
        var slot = content.AddSlot("Port " + port.Name);
        Place(slot, left, top, width, ScriptEditorStyle.RowHeight);
        var button = slot.AttachComponent<Button>();
        var handle = slot.AttachComponent<ScriptCardHandle>();
        handle.Node = node;
        handle.Port = port.Name;
        handle.Kind = port.Kind;
        _handles.Add(handle);

        var dot = slot.AddSlot("Dot");
        float size = ScriptEditorStyle.HandleSize;
        Place(dot, dotX - left, (ScriptEditorStyle.RowHeight - size) * 0.5f, size, size);
        var image = dot.AttachComponent<Image>();
        image.Tint.Value = port.Kind == ScriptCardHandleKind.Variable ? DashTheme.Warning : ScriptEditorStyle.Type(port.Type);
        image.CornerRadius.Value = port.Type == null ? 2f : size * 0.5f;
        button.SetupBackgroundColor(image.Tint);
    }

    // A typed constant is a text field; an object input is a row a reference card drops onto.
    private void ValueEditor(Slot content, FontProvider font, ScriptGraphNode node, string port, ScriptType type, float x, float top)
    {
        var entry = node.FindValue(port);
        // Lists, dictionaries and JSON are made by nodes and never picked up in the world, so an unwired
        // one has nothing to drop in: the row says what the node gets, which is none.
        if (typeof(ScriptCollection).IsAssignableFrom(type.ClrType))
        {
            var none = Builder(content, font).Text("none", ScriptEditorStyle.FontSize - 3f, DashTheme.TextMuted);
            Place(none.RectTransform, x, top + 3f, EditorWidth, ScriptEditorStyle.RowHeight - 6f);
            none.HorizontalAlignment.Value = TextHorizontalAlignment.Center;
            none.VerticalAlignment.Value = TextVerticalAlignment.Middle;
            return;
        }
        if (type.Name == "texture" && node.Space is { Kind.Value: GraphKind.Shader } shaderSpace)
        {
            TextureSlot(content, font, node, shaderSpace, x, top);
            return;
        }
        if (type.ClrType.IsEnum && node.Space is { Kind.Value: GraphKind.Shader })
        {
            Choice(content, font, node, port, type, x, top);
            return;
        }
        if (!ScriptValueText.IsTyped(type))
        {
            var drop = content.AddSlot("Drop " + port);
            Place(drop, x, top + 3f, EditorWidth, ScriptEditorStyle.RowHeight - 6f);
            var image = drop.AttachComponent<Image>();
            image.Tint.Value = DashTheme.Field;
            image.CornerRadius.Value = 6f;
            drop.AttachComponent<Mask>().ShowMaskGraphic.Value = true;
            drop.AttachComponent<Button>();
            var receiver = drop.AttachComponent<ScriptPortDrop>();
            receiver.Node = node;
            receiver.Port = port;
            receiver.Type = type;
            var bound = ScriptValueText.Display(entry);
            var dropUi = Builder(drop, font);
            var label = dropUi.Text(bound.Length > 0 ? Shorten(bound, 16) : "drop " + type.Name, ScriptEditorStyle.FontSize - 3f,
                bound.Length > 0 ? DashTheme.Text : DashTheme.TextMuted);
            Fill(label.Slot);
            label.HorizontalAlignment.Value = TextHorizontalAlignment.Center;
            label.VerticalAlignment.Value = TextVerticalAlignment.Middle;
            return;
        }

        var ui = Builder(content, font);
        // A text field takes line breaks: enter starts a new line, and clicking away keeps what was typed.
        var input = ui.TextInput(ScriptValueText.Display(entry), type.Name, null, type == ScriptTypes.String, DashTheme.Field);
        Place(input.Slot, x, top + 3f, EditorWidth, ScriptEditorStyle.RowHeight - 6f);
        ClipField(input);
        // What is typed goes into the node as it is typed, whenever it reads as a value, so the graph
        // follows the field; half-typed text that does not parse yet waits for more. The whole typing
        // session is one undo step, closed when the field lets go of focus however that happens, and text
        // that never parsed goes back to the node's value then.
        string before = string.Empty;
        input.EditingStarted += _ =>
        {
            _editing++;
            before = node.IsDestroyed ? string.Empty : node.FindValue(port)?.Json.Value ?? string.Empty;
        };
        input.EditingChanged += (_, text) => Apply(node, port, type, text);
        input.FocusLost += (_, text) =>
        {
            _editing = System.Math.Max(0, _editing - 1);
            Apply(node, port, type, text);
            RecordSession(node, port, before);
            if (!node.IsDestroyed && !input.IsDestroyed)
            {
                var shown = ScriptValueText.Display(node.FindValue(port));
                if (input.Text.Value != shown)
                    input.Text.Value = shown;
            }
        };
    }

    // A shader node's texture: the material's property for it, as a small picture and a name, taking a held
    // texture the way the material panel's row does. A loose group of shader nodes has no material yet, and
    // its slots say so.
    private void TextureSlot(Slot content, FontProvider font, ScriptGraphNode node, ScriptGraphSpace space, float x, float top)
    {
        float height = ScriptEditorStyle.RowHeight - 6f;
        var slot = content.AddSlot("Texture");
        Place(slot, x, top + 3f, EditorWidth, height);
        var well = slot.AttachComponent<Image>();
        well.Tint.Value = DashTheme.Field;
        well.CornerRadius.Value = 6f;
        slot.AttachComponent<Mask>().ShowMaskGraphic.Value = true;
        slot.AttachComponent<Button>();

        float side = height - 4f;
        var frame = slot.AddSlot("Checker");
        Place(frame, 2f, 2f, side, side);
        var checker = frame.AttachComponent<Lumora.Core.Assets.CheckerTextureProvider>();
        checker.Width.Value = 16;
        checker.Height.Value = 16;
        checker.CellSize.Value = 4;
        checker.ColorA.Value = new color(0.20f, 0.20f, 0.24f, 1f);
        checker.ColorB.Value = new color(0.12f, 0.12f, 0.15f, 1f);
        frame.AttachComponent<RawImage>().Texture.Target = checker;
        var picture = frame.AddSlot("Picture");
        Fill(picture);
        var raw = picture.AttachComponent<RawImage>();

        var label = Builder(slot, font).Text(string.Empty, ScriptEditorStyle.FontSize - 3f, DashTheme.TextMuted);
        Place(label.RectTransform, side + 8f, 0f, EditorWidth - side - 10f, height);
        label.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        label.VerticalAlignment.Value = TextVerticalAlignment.Middle;

        string? name = null;
        if (node.FindValue("name") is { } named && !string.IsNullOrEmpty(named.Json.Value))
        {
            try
            {
                name = GraphValue.ParseJson(named.Json.Value).String;
            }
            catch (GraphFormatException)
            {
            }
        }
        var receiver = slot.AttachComponent<ShaderTextureSlot>();
        receiver.Source = space.ShaderSource.Target;
        receiver.Uniform = Lumora.Shading.ImageTexture.UniformName(node.NodeId.Value ?? string.Empty, name);
        receiver.Picture = raw;
        receiver.Label = label;
    }

    // A shader node's setting, stepped through its choices with the arrows either side of it: a hand in VR
    // steps through Opaque, Cutout, Transparent faster than it types one.
    private void Choice(Slot content, FontProvider font, ScriptGraphNode node, string port, ScriptType type, float x, float top)
    {
        float height = ScriptEditorStyle.RowHeight - 6f;
        var ui = Builder(content, font);
        var previous = ui.Button("\u2039", (_, _) => StepChoice(node, port, type, -1), DashTheme.Field);
        Place(previous.Slot, x, top + 3f, 26f, height);
        var shown = Builder(content, font).Text(CurrentChoice(node, port, type), ScriptEditorStyle.FontSize - 2f, DashTheme.Text);
        Place(shown.RectTransform, x + 28f, top + 3f, EditorWidth - 56f, height);
        shown.HorizontalAlignment.Value = TextHorizontalAlignment.Center;
        shown.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        shown.AutoSizeHorizontal.Value = true;
        shown.AutoSizeMin.Value = 10f;
        var next = ui.Button("\u203a", (_, _) => StepChoice(node, port, type, 1), DashTheme.Field);
        Place(next.Slot, x + EditorWidth - 26f, top + 3f, 26f, height);
    }

    private static string CurrentChoice(ScriptGraphNode node, string port, ScriptType type)
    {
        var names = Enum.GetNames(type.ClrType);
        if (node.FindValue(port) is { } entry && !string.IsNullOrEmpty(entry.Json.Value))
        {
            try
            {
                if (GraphValue.ParseJson(entry.Json.Value).String is { } text)
                {
                    foreach (var name in names)
                    {
                        if (string.Equals(name, text, StringComparison.OrdinalIgnoreCase))
                            return name;
                    }
                }
            }
            catch (GraphFormatException)
            {
            }
        }
        var overload = node.Overload;
        int index = overload?.InputIndex(port) ?? -1;
        return index >= 0 && overload!.Inputs[index].Default is { } fallback ? fallback.ToString() ?? names[0] : names[0];
    }

    private static void StepChoice(ScriptGraphNode node, string port, ScriptType type, int step)
    {
        if (node.IsDestroyed)
            return;
        var names = Enum.GetNames(type.ClrType);
        int at = System.Math.Max(0, Array.IndexOf(names, CurrentChoice(node, port, type)));
        string chosen = names[((at + step) % names.Length + names.Length) % names.Length];
        string before = node.FindValue(port)?.Json.Value ?? string.Empty;
        try
        {
            node.SetValue(port, GraphValue.Of(chosen));
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }
        if (node.FindValue(port) is { } entry && entry.Json.Value != before)
            InspectorUndo.Record(node, new FieldEditUndoBatch(entry.Json, before, entry.Json.Value, ScriptGraphEditing.ValueStep));
    }

    private static void Apply(ScriptGraphNode node, string port, ScriptType type, string text)
    {
        if (node.IsDestroyed)
            return;
        try
        {
            var entry = node.FindValue(port);
            string current = entry?.Json.Value ?? string.Empty;
            bool bound = entry != null && !entry.Reference.Value.IsNull;
            // Nothing typed that differs from what the node holds, so a field only visited changes nothing.
            if (text == ScriptValueText.Display(entry))
                return;
            // Text is kept exactly as typed, spaces and line breaks and all, and an emptied text field is the
            // empty string. Anything else left blank clears the value.
            if (type != ScriptTypes.String && string.IsNullOrWhiteSpace(text))
            {
                if (current.Length > 0 || bound)
                    node.ClearValue(port);
            }
            else if (ScriptValueText.TryParse(text, type, out var value, out _) && (value.ToJson() != current || bound))
            {
                node.SetValue(port, value);
            }
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void RecordSession(ScriptGraphNode node, string port, string before)
    {
        if (node.IsDestroyed)
            return;
        var entry = node.FindValue(port);
        if (entry != null && entry.Json.Value != before)
            InspectorUndo.Record(node, new FieldEditUndoBatch(entry.Json, before, entry.Json.Value, ScriptGraphEditing.ValueStep));
    }

    private static string VariableName(ScriptGraphNode variable)
    {
        var name = variable.FindValue("name");
        var text = ScriptValueText.Display(name);
        return text.Length > 0 ? text : variable.NodeId.Value ?? "?";
    }

    private static UIBuilder Builder(Slot slot, FontProvider font)
    {
        var ui = new UIBuilder(slot);
        ui.Font(font).FontSize(ScriptEditorStyle.FontSize).TextColor(DashTheme.Text).ForegroundColor(DashTheme.Accent)
            .BackgroundColor(DashTheme.Surface).CornerRadius(6f);
        return ui;
    }

    // Labels have a real clip window. A long custom port name can shrink within its column but can
    // never paint over a value field or another port. The editor itself keeps its full value intact.
    private static Text Label(Slot parent, FontProvider font, string value, float x, float y, float width,
        float height, color tint, TextHorizontalAlignment alignment = TextHorizontalAlignment.Left, float size = ScriptEditorStyle.FontSize)
    {
        var viewport = parent.AddSlot("Label " + value);
        Place(viewport, x, y, width, height);
        viewport.AttachComponent<Mask>();
        var text = Builder(viewport, font).Text(value, size, tint);
        Fill(text.Slot);
        text.WordWrap.Value = false;
        text.AutoSizeHorizontal.Value = true;
        text.AutoSizeMin.Value = 12f;
        text.HorizontalAlignment.Value = alignment;
        text.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        return text;
    }

    private static void ClipField(TextInput input)
    {
        input.Slot.AttachComponent<Mask>().ShowMaskGraphic.Value = true;
        var text = input.Slot.FindChild("Text")?.GetComponent<Text>();
        if (text != null)
        {
            text.Size.Value = 13f;
            text.WordWrap.Value = false;
            text.KeepCaretVisible.Value = true;
        }
    }

    private static string Shorten(string text, int max) => text.Length <= max ? text : text[..(max - 3)] + "...";

    private static void Place(Slot slot, float x, float y, float w, float h)
        => Place(slot.GetComponent<RectTransform>() ?? slot.AttachComponent<RectTransform>(), x, y, w, h);

    // Top-left anchored, y down from the parent's top edge.
    private static void Place(RectTransform? rect, float x, float y, float w, float h)
    {
        if (rect == null)
            return;
        rect.AnchorMin.Value = rect.AnchorMax.Value = new float2(0f, 1f);
        rect.OffsetMin.Value = new float2(x, -(y + h));
        rect.OffsetMax.Value = new float2(x + w, -y);
    }

    private static void Fill(Slot slot)
    {
        var rect = slot.GetComponent<RectTransform>() ?? slot.AttachComponent<RectTransform>();
        rect.AnchorMin.Value = float2.Zero;
        rect.AnchorMax.Value = float2.One;
        rect.OffsetMin.Value = rect.OffsetMax.Value = float2.Zero;
    }
}

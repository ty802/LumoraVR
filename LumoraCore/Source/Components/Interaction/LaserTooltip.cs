// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Assets;
using Lumora.Core.Components.Assets;
using Lumora.Core.Components.Import;
using Lumora.Core.Components.UI;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Interaction;

// The name of what a laser rests on, written beside its cursor once the beam has stayed on the same
// thing for EngineSettings.TooltipDelay. It is this machine's alone: the label is a local slot under the
// beam's end, so nobody else sees it and nothing about it is saved. Text written for the pointer (a
// control's tip, a touch control's label) always shows; a plain object shows its name only when the name
// says something, because imports and spawners leave a lot of "Slot (3)" and "Cube.001" lying about.
// A press or a load on the beam puts it away until the beam moves on to something else. -xlinka
internal sealed class LaserTooltip
{
    private const string FallbackFontPath = "res://Assets/Fonts/FiraCode/FiraCode-SemiBold.ttf";

    // Sizes are in the cursor's own units: the laser scales the cursor with distance so it keeps one size
    // on screen, and the label rides the same scale. An em of 0.024 is about 1.4 cm at a metre.
    private const float Em = 0.024f;
    private const float GapRight = 0.016f;
    private const float RaiseUp = 0.004f;

    // Just above the cursor, in the same band scheme the cursor uses (UserspaceRenderBands).
    private const int RenderQueue = 4006;
    private const int Band = UserspaceRenderBands.Cursor + 1;

    private static readonly color TextColor = new(0.96f, 0.98f, 1f, 1f);

    private Slot? _slot;
    private TextRenderer? _text;
    private MeshRenderer? _renderer;
    private bool _shown;

    private IInteractionTarget? _target;
    private string? _key;
    private float _rested;
    private bool _dismissed;

    // Called once per cast. text is what the target offers (see TextFor), null for nothing worth saying.
    // busy is a press or a load; hidden is a beam that is not drawn this frame.
    public void Update(Slot? pointSlot, IInteractionTarget? target, string? text, bool busy, bool hidden, float delta)
    {
        if (!EngineSettings.Tooltips || target == null || string.IsNullOrEmpty(text) || pointSlot == null || pointSlot.IsDestroyed)
        {
            Forget();
            Hide();
            return;
        }

        if (!ReferenceEquals(target, _target) || !string.Equals(text, _key, StringComparison.Ordinal))
        {
            _target = target;
            _key = text;
            _rested = 0f;
            _dismissed = false;
        }

        if (busy)
            _dismissed = true;
        if (hidden)
            _rested = 0f;
        else
            _rested += MathF.Max(delta, 0f);

        if (_dismissed || hidden || _rested < EngineSettings.TooltipDelay)
        {
            Hide();
            return;
        }

        if (!EnsureBuilt(pointSlot))
            return;
        if (!string.Equals(_text!.Text.Value, text, StringComparison.Ordinal))
            _text.Text.Value = text;
        Show();
    }

    // Beside the cursor, in the view's plane, at the cursor's scale. Called after the laser has placed and
    // sized the cursor for the frame.
    public void Place(float3 point, floatQ viewRotation, float cursorScale, bool lift)
    {
        if (!_shown || _slot == null || _slot.IsDestroyed)
            return;

        float3 right = viewRotation * float3.Right;
        float3 up = viewRotation * float3.Up;
        _slot.GlobalPosition = point + (right * GapRight + up * RaiseUp) * cursorScale;
        _slot.GlobalRotation = viewRotation;
        _slot.GlobalScale = float3.One * cursorScale;
        ApplyBand(lift);
    }

    public void Hide()
    {
        if (!_shown)
            return;
        _shown = false;
        if (_slot != null && !_slot.IsDestroyed)
            _slot.ActiveSelf.Value = false;
    }

    private void Show()
    {
        if (_shown || _slot == null)
            return;
        _shown = true;
        _slot.ActiveSelf.Value = true;
    }

    private void Forget()
    {
        _target = null;
        _key = null;
        _rested = 0f;
        _dismissed = false;
    }

    private bool EnsureBuilt(Slot pointSlot)
    {
        if (_slot != null && !_slot.IsDestroyed && _text != null && !_text.IsDestroyed)
            return true;

        _slot = pointSlot.FindChild("Tooltip") ?? pointSlot.AddLocalSlot("Tooltip");
        _slot.Persistent.Value = false;
        _slot.ActiveSelf.Value = false;
        _shown = false;
        _renderer = null;

        _text = _slot.GetComponent<TextRenderer>();
        if (_text != null)
            return true;

        // Text with no font renders nothing, so the label brings its own provider.
        var font = _slot.AttachComponent<FontProvider>();
        font.URL.Value = ImportDialog.ResolveFontUrl(_slot.World) ?? new Uri(FallbackFontPath);

        // Drawn over whatever the beam is on, as the cursor is: a label sunk into the panel it names is no use.
        var style = _slot.AttachComponent<TextMaterial>();
        style.ZTest.Value = ZTest.Always;
        style.ZWrite.Value = ZWrite.Off;
        style.BlendMode.Value = BlendMode.Alpha;
        style.Culling.Value = Culling.None;
        style.RenderQueue.Value = RenderQueue;
        style.OutlineColor.Value = new colorHDR(0f, 0f, 0f, 0.92f);
        style.OutlineThickness.Value = 1.1f;

        _text = _slot.AttachComponent<TextRenderer>();
        _text.Font.Target = font;
        _text.Style.Target = style;
        _text.Size.Value = Em;
        _text.Color.Value = TextColor;
        _text.HorizontalAlign.Value = Helio.UI.TextHorizontalAlignment.Left;
        _text.VerticalAlign.Value = Helio.UI.TextVerticalAlignment.Middle;
        return true;
    }

    // The text renderer builds its own renderer slot when it starts, so it is looked up until it is there.
    private void ApplyBand(bool lift)
    {
        if (_renderer == null || _renderer.IsDestroyed)
        {
            _renderer = _slot?.FindChild("TextRenderer")?.GetComponent<MeshRenderer>();
            if (_renderer == null)
                return;
            _renderer.SortingOrder.Value = Band - RenderQueue;
        }

        if (lift)
            _renderer.SetOverlayOrder(Band, depthTest: false);
        else
            _renderer.ClearOverlayOrder();
    }

    // Names a surface gives itself, and the ones a slot is born with or an import hands out.
    private static readonly HashSet<string> GenericNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Slot", "New Slot", "Root", "Object", "GameObject", "Node", "Mesh", "Model", "Visual", "Holder",
        "Grabbable", "Unnamed", "Untitled", "Dashboard", "Panel", "Canvas",
        "Box", "Cube", "Sphere", "Cylinder", "Capsule", "Plane", "Quad", "polySurface", "pCube", "pSphere",
    };

    // What the label says for this target, or null. A panel or a handle only speaks through text written
    // for it: its slot name is layout, not a label.
    public static string? TextFor(IInteractionTarget target, in InteractionDescription description)
    {
        string? name = description.Name?.Trim();
        if (string.IsNullOrEmpty(name))
            return null;
        if (target is ILaserPointerTarget && target is Component { Slot: { } slot }
            && string.Equals(name, slot.SlotName.Value?.Trim(), StringComparison.Ordinal))
            return null;
        return IsGenericName(name) ? null : name;
    }

    private static bool IsGenericName(string name)
    {
        int letters = 0;
        int digits = 0;
        int hex = 0;
        foreach (char c in name)
        {
            if (char.IsLetter(c)) letters++;
            if (char.IsDigit(c)) digits++;
            if (Uri.IsHexDigit(c) || c == '-') hex++;
        }
        if (letters == 0 || digits > letters)
            return true;
        // An id: a long run of hex and dashes.
        if (name.Length >= 12 && hex == name.Length)
            return true;

        // "Slot (2)", "Cube.001", "mesh_12", "pCube1": the generic stem with a counter on the end.
        int end = name.Length;
        while (end > 0 && (char.IsDigit(name[end - 1]) || name[end - 1] is ' ' or '.' or '_' or '-' or '(' or ')'))
            end--;
        return end == 0 || GenericNames.Contains(name.Substring(0, end));
    }
}

// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Text;
using Helio.UI;
using Lumora.Core.Assets;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

// A plate of the context menu made in the world rather than in code: what it says, its colour and icon, and
// what a press does. The press is the ring's own button press, handed to everything on this slot that hears
// presses (a toggle, a value setter, a script listening here) on the presser's machine. With Opens set it
// leads on to a ring of the entries under that slot. A ContextMenuPin puts it on someone's root ring. -xlinka
[ComponentCategory("UI/Context Menu")]
public class ContextMenuEntry : Component
{
    public readonly Sync<string> Label;
    // White keeps the ring's own colours; any other colour lights the plate's edge.
    public readonly Sync<colorHDR> Tint;
    public readonly AssetRef<TextureAsset> Icon;
    public readonly Sync<bool> Interactable;
    public readonly Sync<bool> CloseOnPress;
    // Pressing leads to a ring of the entries on this slot's children, or with Deep of every one under it.
    public readonly SyncRef<Slot> Opens;
    public readonly Sync<bool> Deep;

    public ContextMenuEntry()
    {
        Label = new Sync<string>(this, "Entry");
        Tint = new Sync<colorHDR>(this, colorHDR.White);
        Icon = new AssetRef<TextureAsset>(this);
        Interactable = new Sync<bool>(this, true);
        CloseOnPress = new Sync<bool>(this, false);
        Opens = new SyncRef<Slot>(this);
        Deep = new Sync<bool>(this, false);
    }

    // The plate as the ring shows it, kept up to date by the ring while it is open.
    public ContextMenuItem ToMenuItem()
    {
        var item = new ContextMenuItem { SafeMarkup = true, Group = ContextMenuGroup.Self };
        Show(item);
        item.Refresh = Show;
        item.KeepsOpen = !CloseOnPress.Value || Opens.Target != null;
        item.OnButton = Press;
        return item;
    }

    // Writes what the plate shows now into the item; true when any of it changed.
    private bool Show(ContextMenuItem item)
    {
        if (IsDestroyed)
            return false;
        string label = Sanitize(Label.Value);
        var tint = Tint.Value;
        float[]? outline = tint.r >= 0.99f && tint.g >= 0.99f && tint.b >= 0.99f ? null : new[] { tint.r, tint.g, tint.b, 1f };
        bool enabled = Interactable.Value;
        var icon = Icon.Target;
        bool opens = Opens.Target != null;
        bool changed = item.Label != label || item.IsEnabled != enabled || !ReferenceEquals(item.IconAsset, icon)
            || !SameColor(item.OutlineColor, outline) || item.OpensPage != opens;
        item.Label = label;
        item.OpensPage = opens;
        item.OutlineColor = outline;
        item.IsEnabled = enabled;
        item.IconAsset = icon;
        return changed;
    }

    private static bool SameColor(float[]? a, float[]? b)
    {
        if (a == null || b == null)
            return a == b;
        for (int i = 0; i < 4; i++)
        {
            if (MathF.Abs(a[i] - b[i]) > 0.001f)
                return false;
        }
        return true;
    }

    private void Press(ContextMenuSystem menu, Button button, UIInteractionContext context)
    {
        if (IsDestroyed || !Enabled.Value || !Interactable.Value)
            return;
        var slot = Slot;
        foreach (var component in slot.GetComponents<Component>())
        {
            if (component is not IButtonPressReceiver receiver || component.IsDestroyed || !component.Enabled.Value)
                continue;
            try
            {
                receiver.OnButtonPressed(button, in context);
            }
            catch (Exception ex)
            {
                Logging.Logger.Error($"ContextMenuEntry: a press receiver threw: {ex.Message}");
            }
        }
        if (!IsDestroyed && !slot.IsDestroyed)
            new Interaction.PressEvent(this, context.Actor, context.WorldPoint).Send(slot);
        if (Branch() is { } page)
            menu.OpenBranch(page);
    }

    // The ring this entry leads to, built as it stands now. Entries under someone else's user root stay off
    // this ring.
    public ContextMenuPage? Branch()
    {
        var root = Opens.Target;
        if (root == null || root.IsDestroyed)
            return null;
        var page = new ContextMenuPage(Plain(Label.Value)) { Key = root };
        var found = new List<ContextMenuEntry>();
        if (Deep.Value)
            Collect(root, found);
        else
        {
            foreach (var child in root.Children)
            {
                foreach (var component in child.Components)
                {
                    if (component is ContextMenuEntry entry && entry.Enabled.Value)
                    {
                        found.Add(entry);
                        break;
                    }
                }
            }
        }
        var local = World?.LocalUser;
        foreach (var entry in found)
        {
            if (entry.Slot.ActiveUserRoot is { } owner && !ReferenceEquals(owner.ActiveUser, local))
                continue;
            page.AddItem(entry.ToMenuItem());
        }
        return page.Items.Count > 0 ? page : null;
    }

    private static void Collect(Slot slot, List<ContextMenuEntry> into)
    {
        foreach (var component in slot.Components)
        {
            if (component is ContextMenuEntry entry && entry.Enabled.Value)
                into.Add(entry);
        }
        foreach (var child in slot.Children)
            Collect(child, into);
    }

    private static readonly HashSet<string> KeptTags = new(StringComparer.OrdinalIgnoreCase) { "color", "alpha", "b", "i", "u", "s" };

    // Text typed into the world keeps its colours and emphasis on the ring and nothing else: no sizes, no
    // pictures, no tags that reach past the label. A colour named by a dotted palette name keeps its last
    // part.
    public static string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        var result = new StringBuilder(text.Length);
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            int close = c == '<' ? text.IndexOf('>', i + 1) : -1;
            if (close < 0)
            {
                result.Append(c);
                i++;
                continue;
            }
            string inner = text.Substring(i + 1, close - i - 1).Trim();
            bool ending = inner.StartsWith('/');
            string body = ending ? inner[1..].Trim() : inner;
            int equals = body.IndexOf('=');
            string name = (equals >= 0 ? body[..equals] : body).Trim();
            if (name.Length > 0 && IsWord(name))
            {
                if (KeptTags.Contains(name))
                {
                    if (ending || equals < 0)
                        result.Append('<').Append(ending ? "/" : "").Append(name.ToLowerInvariant()).Append('>');
                    else
                    {
                        string value = body[(equals + 1)..].Trim().Trim('"', '\'');
                        if (!value.StartsWith('#') && value.LastIndexOf('.') is int dot and >= 0)
                            value = value[(dot + 1)..];
                        result.Append('<').Append(name.ToLowerInvariant()).Append('=').Append(value).Append('>');
                    }
                }
                i = close + 1;
                continue;
            }
            result.Append(c);
            i++;
        }
        return result.ToString();
    }

    // The label with every tag taken out, for a ring's title.
    public static string Plain(string? text)
    {
        string kept = Sanitize(text);
        var result = new StringBuilder(kept.Length);
        bool inTag = false;
        foreach (char c in kept)
        {
            if (c == '<')
                inTag = true;
            else if (c == '>' && inTag)
                inTag = false;
            else if (!inTag)
                result.Append(c);
        }
        return result.ToString();
    }

    private static bool IsWord(string name)
    {
        foreach (char c in name)
        {
            if (!char.IsLetter(c))
                return false;
        }
        return true;
    }
}

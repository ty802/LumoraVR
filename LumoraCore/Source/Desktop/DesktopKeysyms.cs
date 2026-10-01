// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Lumora.Core.Desktop;

// Characters and keys as X keysyms, for both Linux desktops.
//
// Keyboard layouts carry the old named keysyms (the euro sign, Cyrillic, Greek, kana), not the Unicode
// ones, and a compositor looking for a key matches keysyms exactly, so a character has to be sent the
// way the layout spells it. libxkbcommon's own table knows every one of those spellings; it is asked
// first, and the plain rule (Latin-1 as itself, the rest flagged Unicode) only stands in where the
// library is missing. -xlinka
[SupportedOSPlatform("linux")]
internal static unsafe class DesktopKeysyms
{
    public const uint BackSpace = 0xFF08, Tab = 0xFF09, Return = 0xFF0D, Escape = 0xFF1B, Delete = 0xFFFF;
    public const uint Left = 0xFF51, Up = 0xFF52, Right = 0xFF53, Down = 0xFF54;
    public const uint ShiftLeft = 0xFFE1, ControlLeft = 0xFFE3, SuperLeft = 0xFFEB;
    public const uint LetterC = 0x63, LetterV = 0x76;

    private static readonly delegate* unmanaged[Cdecl]<uint, uint> FromUtf32 = Resolve();

    private static delegate* unmanaged[Cdecl]<uint, uint> Resolve()
    {
        if (NativeLibrary.TryLoad("libxkbcommon.so.0", out var library)
            && NativeLibrary.TryGetExport(library, "xkb_utf32_to_keysym", out var function))
            return (delegate* unmanaged[Cdecl]<uint, uint>)function;
        return null;
    }

    // Zero for anything that is not a character to type.
    public static uint Of(Rune rune)
    {
        // A line break is Enter to whoever is typing, whatever the table calls it.
        if (rune.Value is '\n' or '\r')
            return Return;
        if (FromUtf32 != null)
            return FromUtf32((uint)rune.Value);
        return rune.Value switch
        {
            '\b' => BackSpace,
            '\t' => Tab,
            0x1B => Escape,
            0x7F => Delete,
            >= 0x20 and <= 0x7E or >= 0xA0 and <= 0xFF => (uint)rune.Value,
            < 0x100 => 0,
            _ => 0x01000000u | (uint)rune.Value,
        };
    }

    public static uint Of(DesktopKey key) => key switch
    {
        DesktopKey.Backspace => BackSpace,
        DesktopKey.Tab => Tab,
        DesktopKey.Enter => Return,
        DesktopKey.Escape => Escape,
        DesktopKey.Left => Left,
        DesktopKey.Right => Right,
        DesktopKey.Up => Up,
        DesktopKey.Down => Down,
        DesktopKey.Copy => LetterC,
        DesktopKey.Paste => LetterV,
        _ => SuperLeft,
    };
}

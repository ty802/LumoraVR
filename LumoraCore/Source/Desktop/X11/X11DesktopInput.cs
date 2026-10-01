// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Threading;

namespace Lumora.Core.Desktop;

// The mouse and keyboard on X11, through XTest: the same path a physical device's events take into the
// server, so every client sees ordinary input.
//
// Typing goes by keysym, not by key. A character the active layout has is found on its key, in the layout
// group that is active right now (a second layout switched on would otherwise turn "hello" into
// "руддщ"). One it lacks borrows a spare keycode. Borrowed keys stay bound while typing goes on and are
// emptied once it has paused: a program reads the new mapping when it gets round to it, and a key
// emptied straight after its press can be read as nothing at all. -xlinka
[SupportedOSPlatform("linux")]
internal sealed unsafe class X11DesktopInput : IDesktopInput, IDisposable
{
    private const nuint AtomCardinal = 6;
    private const int BorrowedKeys = 8;
    private static readonly TimeSpan BorrowedHold = TimeSpan.FromMilliseconds(400);

    private readonly object _gate = new();
    private readonly Dictionary<nuint, byte> _borrowed = new();
    private readonly Queue<byte> _borrowOrder = new();
    private readonly Timer _release;
    private IntPtr _x;
    private nuint _root;
    private nuint _pidAtom;
    private int _wheelY;
    private int _wheelX;

    public X11DesktopInput()
    {
        _release = new Timer(_ => ReleaseBorrowed(), null, Timeout.Infinite, Timeout.Infinite);
    }

    private bool Connect()
    {
        if (_x != IntPtr.Zero)
            return true;
        _x = X11Native.Open();
        if (_x == IntPtr.Zero)
            return false;
        _root = X11Native.XRootWindow(_x, X11Native.XDefaultScreen(_x));
        fixed (byte* name = "_NET_WM_PID\0"u8)
            _pidAtom = X11Native.XInternAtom(_x, name, 1);
        return true;
    }

    // Keymap changes arrive as events on this connection, and Xlib's cached map is only renewed when
    // they are handed back to it.
    private void Drain()
    {
        XEvent ev;
        while (X11Native.XPending(_x) > 0)
        {
            X11Native.XNextEvent(_x, &ev);
            if (ev.Type == X11Native.MappingNotify)
                X11Native.XRefreshKeyboardMapping(&ev);
        }
    }

    public bool MoveTo(int x, int y)
    {
        lock (_gate)
        {
            if (!Connect())
                return false;
            X11Native.XTestFakeMotionEvent(_x, -1, x, y, 0);
            X11Native.XFlush(_x);
            return true;
        }
    }

    public bool Button(DesktopButton button, bool down)
    {
        lock (_gate)
        {
            if (!Connect())
                return false;
            uint number = button switch
            {
                DesktopButton.Right => 3u,
                DesktopButton.Middle => 2u,
                _ => 1u,
            };
            X11Native.XTestFakeButtonEvent(_x, number, down ? 1 : 0, 0);
            X11Native.XFlush(_x);
            return true;
        }
    }

    // X has no fine scrolling: a detent is a click of buttons 4 and 5 (6 and 7 sideways), so steps
    // smaller than one are saved up until they make one.
    public bool Wheel(int delta, bool horizontal)
    {
        lock (_gate)
        {
            if (!Connect())
                return false;
            ref int owed = ref horizontal ? ref _wheelX : ref _wheelY;
            owed += delta;
            while (System.Math.Abs(owed) >= IDesktopInput.WheelStep)
            {
                bool positive = owed > 0;
                uint number = horizontal ? (positive ? 7u : 6u) : (positive ? 4u : 5u);
                X11Native.XTestFakeButtonEvent(_x, number, 1, 0);
                X11Native.XTestFakeButtonEvent(_x, number, 0, 0);
                owed -= positive ? IDesktopInput.WheelStep : -IDesktopInput.WheelStep;
            }
            X11Native.XFlush(_x);
            return true;
        }
    }

    public bool TryGetCursor(out int x, out int y)
    {
        lock (_gate)
        {
            x = y = 0;
            if (!Connect())
                return false;
            return X11Native.XQueryPointer(_x, _root, out _, out _, out x, out y, out _, out _, out _) != 0;
        }
    }

    // The window holding focus, or the first of its ancestors that names its process.
    public bool OwnWindowFocused()
    {
        lock (_gate)
        {
            if (!Connect() || _pidAtom == 0)
                return false;
            X11Native.XGetInputFocus(_x, out nuint window, out _);
            for (int depth = 0; depth < 32 && window > 1; depth++)
            {
                int pid = ProcessOf(window);
                if (pid >= 0)
                    return pid == Environment.ProcessId;
                if (X11Native.XQueryTree(_x, window, out nuint root, out nuint parent, out nuint* children, out _) == 0)
                    return false;
                if (children != null)
                    X11Native.XFree(children);
                if (window == root)
                    return false;
                window = parent;
            }
            return false;
        }
    }

    private int ProcessOf(nuint window)
    {
        if (X11Native.XGetWindowProperty(_x, window, _pidAtom, 0, 1, 0, AtomCardinal,
            out _, out int format, out nuint items, out _, out byte* data) != 0)
            return -1;
        try
        {
            // Format-32 properties come back as an array of longs, whatever the name says.
            return data != null && format == 32 && items == 1 ? (int)*(nuint*)data : -1;
        }
        finally
        {
            if (data != null)
                X11Native.XFree(data);
        }
    }

    public bool Type(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;
        lock (_gate)
        {
            if (!Connect() || OwnWindowFocused())
                return false;
            Drain();
            int group = ActiveGroup();
            bool all = true;
            foreach (var rune in text.EnumerateRunes())
            {
                uint keysym = DesktopKeysyms.Of(rune);
                all &= keysym != 0 && Stroke(keysym, group);
            }
            X11Native.XFlush(_x);
            return all;
        }
    }

    public bool Press(DesktopKey key)
    {
        lock (_gate)
        {
            if (!Connect() || (key != DesktopKey.Start && OwnWindowFocused()))
                return false;
            Drain();
            int group = ActiveGroup();
            bool done = key is DesktopKey.Copy or DesktopKey.Paste
                ? Chord(DesktopKeysyms.ControlLeft, DesktopKeysyms.Of(key), group)
                : Stroke(DesktopKeysyms.Of(key), group);
            X11Native.XFlush(_x);
            return done;
        }
    }

    private int ActiveGroup()
    {
        XkbState state;
        return X11Native.XkbGetState(_x, X11Native.XkbUseCoreKeyboard, &state) == 0 ? state.Group : 0;
    }

    private bool Chord(nuint modifier, nuint keysym, int group)
    {
        byte held = X11Native.XKeysymToKeycode(_x, modifier);
        if (held == 0)
            return false;
        X11Native.XTestFakeKeyEvent(_x, held, 1, 0);
        bool done = Stroke(keysym, group);
        X11Native.XTestFakeKeyEvent(_x, held, 0, 0);
        return done;
    }

    private bool Stroke(nuint keysym, int group)
    {
        byte code = X11Native.XKeysymToKeycode(_x, keysym);
        int level = code != 0 ? LevelOf(code, keysym, group) : -1;
        if (level < 0)
        {
            code = Borrow(keysym);
            level = 0;
        }
        if (code == 0)
            return false;
        byte shift = level == 1 ? X11Native.XKeysymToKeycode(_x, DesktopKeysyms.ShiftLeft) : (byte)0;
        if (shift != 0)
            X11Native.XTestFakeKeyEvent(_x, shift, 1, 0);
        X11Native.XTestFakeKeyEvent(_x, code, 1, 0);
        X11Native.XTestFakeKeyEvent(_x, code, 0, 0);
        if (shift != 0)
            X11Native.XTestFakeKeyEvent(_x, shift, 0, 0);
        return true;
    }

    // Level 0 or 1 of the key in the group in force; anything further up needs modifiers this does not hold.
    private int LevelOf(byte code, nuint keysym, int group)
    {
        for (int level = 0; level < 2; level++)
        {
            if (X11Native.XkbKeycodeToKeysym(_x, code, group, level) == keysym)
                return level;
        }
        return -1;
    }

    // A spare keycode bound to keysym in every group and level, kept while typing goes on.
    private byte Borrow(nuint keysym)
    {
        _release.Change(BorrowedHold, Timeout.InfiniteTimeSpan);
        if (_borrowed.TryGetValue(keysym, out byte known))
            return known;
        byte code;
        if (_borrowed.Count >= BorrowedKeys)
        {
            code = _borrowOrder.Dequeue();
            foreach (var pair in _borrowed)
            {
                if (pair.Value == code)
                {
                    _borrowed.Remove(pair.Key);
                    break;
                }
            }
        }
        else
        {
            code = SpareKeycode();
            if (code == 0)
                return 0;
        }
        nuint* both = stackalloc nuint[2] { keysym, keysym };
        X11Native.XChangeKeyboardMapping(_x, code, 2, both, 1);
        X11Native.XSync(_x, 0);
        _borrowed[keysym] = code;
        _borrowOrder.Enqueue(code);
        return code;
    }

    private void ReleaseBorrowed()
    {
        lock (_gate)
        {
            if (_x == IntPtr.Zero || _borrowed.Count == 0)
                return;
            nuint* none = stackalloc nuint[2] { 0, 0 };
            foreach (var code in _borrowed.Values)
                X11Native.XChangeKeyboardMapping(_x, code, 2, none, 1);
            X11Native.XSync(_x, 0);
            _borrowed.Clear();
            _borrowOrder.Clear();
        }
    }

    // The highest keycode with nothing on it and not already lent out.
    private byte SpareKeycode()
    {
        X11Native.XDisplayKeycodes(_x, out int minimum, out int maximum);
        int count = maximum - minimum + 1;
        nuint* map = X11Native.XGetKeyboardMapping(_x, (byte)minimum, count, out int perKey);
        if (map == null)
            return 0;
        try
        {
            for (int code = maximum; code >= minimum; code--)
            {
                if (_borrowed.ContainsValue((byte)code))
                    continue;
                bool empty = true;
                for (int i = 0; i < perKey && empty; i++)
                    empty = map[(code - minimum) * perKey + i] == 0;
                if (empty)
                    return (byte)code;
            }
            return 0;
        }
        finally
        {
            X11Native.XFree(map);
        }
    }

    public void Dispose()
    {
        _release.Dispose();
        ReleaseBorrowed();
        lock (_gate)
        {
            X11Native.Close(_x);
            _x = IntPtr.Zero;
        }
    }
}

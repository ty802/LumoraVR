// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Lumora.Core.Desktop;

// The mouse and keyboard on Windows. Everything goes through SendInput, the same queue the physical
// devices feed, so hover effects, drag and drop and double clicks behave exactly as they do by hand.
//
// Coordinates are physical pixels on the virtual desktop, the space the capture reports monitors in. The
// thread is switched to per-monitor awareness for each call so a scaled second monitor does not shift
// every click by its scale factor. Windows refuses input to a program running as administrator when this
// one is not; that is a wall on purpose and nothing here tries to climb it.
//
// Start is let through even while our own window has focus: it opens the Start menu wherever focus is and
// nothing of it comes back as typing. -xlinka
[SupportedOSPlatform("windows")]
internal sealed class WindowsDesktopInput : IDesktopInput
{
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;

    private const uint MouseMove = 0x0001;
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;
    private const uint MouseRightDown = 0x0008;
    private const uint MouseRightUp = 0x0010;
    private const uint MouseMiddleDown = 0x0020;
    private const uint MouseMiddleUp = 0x0040;
    private const uint MouseWheel = 0x0800;
    private const uint MouseHWheel = 0x1000;
    private const uint MouseVirtualDesk = 0x4000;
    private const uint MouseAbsolute = 0x8000;

    private const uint KeyExtended = 0x0001;
    private const uint KeyUp = 0x0002;
    private const uint KeyUnicode = 0x0004;

    private const ushort VkBack = 0x08, VkTab = 0x09, VkReturn = 0x0D, VkControl = 0x11, VkEscape = 0x1B;
    private const ushort VkLeft = 0x25, VkUp = 0x26, VkRight = 0x27, VkDown = 0x28, VkWindows = 0x5B;

    private const int VirtualLeft = 76;
    private const int VirtualTop = 77;
    private const int VirtualWidth = 78;
    private const int VirtualHeight = 79;

    private static readonly IntPtr PerMonitorAwareV2 = new(-4);

    public bool MoveTo(int x, int y)
    {
        using var aware = new AwareScope();
        int left = GetSystemMetrics(VirtualLeft), top = GetSystemMetrics(VirtualTop);
        int width = GetSystemMetrics(VirtualWidth), height = GetSystemMetrics(VirtualHeight);
        if (width <= 0 || height <= 0)
            return false;
        // Rounded up, so the pixel Windows maps the value back to is this one and not its left neighbour.
        int nx = (int)(((long)(x - left) * 65536 + width - 1) / width);
        int ny = (int)(((long)(y - top) * 65536 + height - 1) / height);
        return Send(Mouse(nx, ny, 0, MouseMove | MouseAbsolute | MouseVirtualDesk));
    }

    public bool Button(DesktopButton button, bool down)
    {
        uint flags = button switch
        {
            DesktopButton.Right => down ? MouseRightDown : MouseRightUp,
            DesktopButton.Middle => down ? MouseMiddleDown : MouseMiddleUp,
            _ => down ? MouseLeftDown : MouseLeftUp,
        };
        return Send(Mouse(0, 0, 0, flags));
    }

    public bool Wheel(int delta, bool horizontal)
    {
        if (delta == 0)
            return true;
        return Send(Mouse(0, 0, unchecked((uint)delta), horizontal ? MouseHWheel : MouseWheel));
    }

    // Where the real pointer is, in the same physical pixels.
    public bool TryGetCursor(out int x, out int y)
    {
        using var aware = new AwareScope();
        if (GetCursorPos(out var point) != 0)
        {
            x = point.X;
            y = point.Y;
            return true;
        }
        x = y = 0;
        return false;
    }

    public bool OwnWindowFocused()
    {
        IntPtr window = GetForegroundWindow();
        if (window == IntPtr.Zero)
            return false;
        GetWindowThreadProcessId(window, out uint process);
        return process == (uint)Environment.ProcessId;
    }

    public bool Type(string text)
    {
        if (string.IsNullOrEmpty(text) || OwnWindowFocused())
            return false;
        var inputs = new Input[text.Length * 2];
        for (int i = 0; i < text.Length; i++)
        {
            inputs[i * 2] = Keyboard(0, text[i], KeyUnicode);
            inputs[i * 2 + 1] = Keyboard(0, text[i], KeyUnicode | KeyUp);
        }
        return Send(inputs);
    }

    public bool Press(DesktopKey key)
    {
        if (key != DesktopKey.Start && OwnWindowFocused())
            return false;
        return key switch
        {
            DesktopKey.Copy => Chord(VkControl, 'C'),
            DesktopKey.Paste => Chord(VkControl, 'V'),
            _ => Tap(key switch
            {
                DesktopKey.Backspace => VkBack,
                DesktopKey.Tab => VkTab,
                DesktopKey.Enter => VkReturn,
                DesktopKey.Escape => VkEscape,
                DesktopKey.Left => VkLeft,
                DesktopKey.Right => VkRight,
                DesktopKey.Up => VkUp,
                DesktopKey.Down => VkDown,
                _ => VkWindows,
            }),
        };
    }

    private static bool Tap(ushort vk)
    {
        uint extended = vk is VkLeft or VkUp or VkRight or VkDown or VkWindows ? KeyExtended : 0;
        return Send(new[] { Keyboard(vk, 0, extended), Keyboard(vk, 0, extended | KeyUp) });
    }

    private static bool Chord(ushort modifier, char letter)
    {
        return Send(new[]
        {
            Keyboard(modifier, 0, 0),
            Keyboard(letter, 0, 0),
            Keyboard(letter, 0, KeyUp),
            Keyboard(modifier, 0, KeyUp),
        });
    }

    private static Input Mouse(int x, int y, uint data, uint flags)
        => new() { Type = InputMouse, Data = new InputData { Mouse = new MouseInput { X = x, Y = y, Data = data, Flags = flags } } };

    private static Input Keyboard(ushort vk, ushort scan, uint flags)
        => new() { Type = InputKeyboard, Data = new InputData { Keyboard = new KeyboardInput { Vk = vk, Scan = scan, Flags = flags } } };

    private static unsafe bool Send(Input input) => SendInput(1, &input, sizeof(Input)) == 1;

    private static unsafe bool Send(Input[] inputs)
    {
        fixed (Input* first = inputs)
            return SendInput((uint)inputs.Length, first, sizeof(Input)) == inputs.Length;
    }

    internal static IDisposable PhysicalPixels() => new AwareScope();

    private readonly struct AwareScope : IDisposable
    {
        private readonly IntPtr _previous;

        public AwareScope()
        {
            try
            {
                _previous = SetThreadDpiAwarenessContext(PerMonitorAwareV2);
            }
            catch (EntryPointNotFoundException)
            {
                _previous = IntPtr.Zero;
            }
        }

        public void Dispose()
        {
            if (_previous != IntPtr.Zero)
                SetThreadDpiAwarenessContext(_previous);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint Data;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort Vk;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputData
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputData Data;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern unsafe uint SendInput(uint count, Input* inputs, int size);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int GetCursorPos(out RawPoint point);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
}

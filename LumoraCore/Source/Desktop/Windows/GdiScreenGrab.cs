// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Lumora.Core.Desktop;

// A rectangle of the screen as Windows has it composed right now, read through GDI into a top-down 32-bit
// BGRX buffer. Far slower than duplication, so it is only used to have a picture the moment a capture
// opens: duplication hands its first image over only once something on screen is drawn again. -xlinka
[SupportedOSPlatform("windows")]
internal static unsafe class GdiScreenGrab
{
    private const uint SourceCopy = 0x00CC0020;

    public delegate void Use(byte* bits, long pitch);

    public static bool Grab(int left, int top, int width, int height, Use use)
    {
        if (width <= 0 || height <= 0)
            return false;
        using var physical = WindowsDesktopInput.PhysicalPixels();
        IntPtr screen = GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero)
            return false;
        IntPtr memory = IntPtr.Zero, bitmap = IntPtr.Zero, previous = IntPtr.Zero;
        try
        {
            memory = CreateCompatibleDC(screen);
            var header = new BitmapInfoHeader
            {
                Size = (uint)sizeof(BitmapInfoHeader),
                Width = width,
                Height = -height,
                Planes = 1,
                BitCount = 32,
            };
            void* bits;
            bitmap = CreateDIBSection(screen, &header, 0, &bits, IntPtr.Zero, 0);
            if (memory == IntPtr.Zero || bitmap == IntPtr.Zero || bits == null)
                return false;
            previous = SelectObject(memory, bitmap);
            if (BitBlt(memory, 0, 0, width, height, screen, left, top, SourceCopy) == 0)
                return false;
            use((byte*)bits, (long)width * 4);
            return true;
        }
        finally
        {
            if (previous != IntPtr.Zero)
                SelectObject(memory, previous);
            if (bitmap != IntPtr.Zero)
                DeleteObject(bitmap);
            if (memory != IntPtr.Zero)
                DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern IntPtr CreateDIBSection(IntPtr dc, BitmapInfoHeader* info, uint usage, void** bits, IntPtr section, uint offset);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr item);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern int BitBlt(IntPtr target, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, uint operation);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern int DeleteObject(IntPtr item);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern int DeleteDC(IntPtr dc);
}

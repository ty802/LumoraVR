// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Threading;

namespace Lumora.Core.Desktop;

// One monitor of an X screen. X has no "here is what changed" feed with the pixels attached, so the
// monitor is read whole through shared memory, but only when DAMAGE says something was drawn, and only the
// strips whose rows really differ from the last reading are passed on. A still desktop costs an event
// check and a pointer query a tick.
//
// The cursor is not in the picture X hands over; XFixes gives its image and hotspot. -xlinka
[SupportedOSPlatform("linux")]
internal sealed unsafe class X11DesktopCapture : DesktopCapture
{
    private const int MaxRate = 60;
    private const int MaxCursorSize = 512;

    public X11DesktopCapture(DesktopDisplay display) : base(display)
    {
    }

    protected override void Run(RunFlag run)
    {
        while (!run.Stop)
        {
            IntPtr x = X11Native.Open();
            if (x == IntPtr.Zero)
            {
                SetProblem("The X server cannot be reached.");
                Nap(1000, run);
                continue;
            }
            try
            {
                Pump(x, run);
            }
            finally
            {
                X11Native.Close(x);
            }
            if (!run.Stop)
                Nap(250, run);
        }
    }

    private void Pump(IntPtr x, RunFlag run)
    {
        int screen = X11Native.XDefaultScreen(x);
        nuint root = X11Native.XRootWindow(x, screen);
        int width = Display.Width, height = Display.Height;
        if (Display.Left < 0 || Display.Top < 0 || width <= 0 || height <= 0
            || Display.Left + width > X11Native.XDisplayWidth(x, screen) || Display.Top + height > X11Native.XDisplayHeight(x, screen))
        {
            SetProblem("This monitor is no longer part of the X screen.");
            return;
        }

        using var reader = ScreenReader.Open(x, screen, width, height);
        if (reader == null)
        {
            SetProblem("The X server would not share its screen.");
            return;
        }
        Resize(width, height);
        SetProblem(null);

        nuint damage = 0;
        int damageEvent = -1;
        if (X11Native.Has(X11Native.Xdamage) && X11Native.XDamageQueryExtension(x, out int damageBase, out _) != 0)
        {
            damage = X11Native.XDamageCreate(x, root, X11Native.DamageReportNonEmpty);
            damageEvent = damageBase;
        }
        // The cursor's picture is fetched when X says it changed, not every tick; where it is, is asked
        // every tick, since nothing says that.
        int cursorEvent = -1, fixesBase = 0;
        bool cursors = X11Native.Has(X11Native.Xfixes) && X11Native.XFixesQueryExtension(x, out fixesBase, out _) != 0;
        if (cursors)
        {
            X11Native.XFixesSelectCursorInput(x, root, X11Native.CursorNotifyMask);
            cursorEvent = fixesBase + X11Native.CursorNotify;
        }

        var previous = new byte[(long)width * height * 4];
        bool drawn = true, first = true, cursorChanged = true;
        int hotX = 0, hotY = 0;
        var clock = Stopwatch.StartNew();
        double last = 0;
        try
        {
            while (!run.Stop)
            {
                XEvent ev;
                while (X11Native.XPending(x) > 0)
                {
                    X11Native.XNextEvent(x, &ev);
                    if (ev.Type == damageEvent)
                        drawn = true;
                    else if (ev.Type == cursorEvent)
                        cursorChanged = true;
                }

                if (drawn || damage == 0)
                {
                    // Cleared before the read, so drawing that lands during it is heard next tick.
                    if (damage != 0)
                        X11Native.XDamageSubtract(x, damage, 0, 0);
                    drawn = false;
                    var image = reader.Read(root, Display.Left, Display.Top);
                    if (image == null)
                    {
                        SetProblem("The X server stopped handing over the screen.");
                        return;
                    }
                    try
                    {
                        if (!IsBgrx(image))
                        {
                            SetProblem($"The screen is in a pixel format this cannot read ({image->BitsPerPixel} bits).");
                            return;
                        }
                        PassChangedStrips(image->Data, image->BytesPerLine, width, height, previous, first, true);
                    }
                    finally
                    {
                        reader.Done(image);
                    }
                    if (first)
                    {
                        MarkPicture();
                        first = false;
                    }
                }

                if (cursors && cursorChanged)
                {
                    cursorChanged = false;
                    ReadCursorImage(x, ref hotX, ref hotY);
                }
                ReadPointer(x, root, cursors, hotX, hotY);

                double now = clock.Elapsed.TotalSeconds;
                double wait = 1.0 / MaxRate - (now - last);
                if (wait > 0.001)
                    Thread.Sleep((int)(wait * 1000.0));
                last = clock.Elapsed.TotalSeconds;
            }
        }
        finally
        {
            if (damage != 0)
                X11Native.XDamageDestroy(x, damage);
        }
    }

    private static bool IsBgrx(XImage* image)
        => image->BitsPerPixel == 32 && image->ByteOrder == 0
            && image->RedMask == 0xFF0000 && image->GreenMask == 0xFF00 && image->BlueMask == 0xFF;

    private void ReadPointer(IntPtr x, nuint root, bool cursors, int hotX, int hotY)
    {
        if (X11Native.XQueryPointer(x, root, out _, out _, out int px, out int py, out _, out _, out _) == 0)
        {
            SetPointer(0, 0, false);
            return;
        }
        SetPointer(px - hotX - Display.Left, py - hotY - Display.Top, cursors && Display.Contains(px, py));
    }

    // X11 cursors are premultiplied ARGB, one pixel per unsigned long. The panel blends straight alpha.
    private void ReadCursorImage(IntPtr x, ref int hotX, ref int hotY)
    {
        var cursor = X11Native.XFixesGetCursorImage(x);
        if (cursor == null)
            return;
        try
        {
            if (cursor->Width == 0 || cursor->Height == 0 || cursor->Width > MaxCursorSize || cursor->Height > MaxCursorSize)
                return;
            int w = cursor->Width, h = cursor->Height;
            var pixels = new byte[w * h * 4];
            for (int i = 0; i < w * h; i++)
            {
                uint p = (uint)cursor->Pixels[i];
                uint a = p >> 24;
                uint r = (p >> 16) & 0xFF, g = (p >> 8) & 0xFF, b = p & 0xFF;
                if (a > 0 && a < 255)
                {
                    r = System.Math.Min(255u, r * 255 / a);
                    g = System.Math.Min(255u, g * 255 / a);
                    b = System.Math.Min(255u, b * 255 / a);
                }
                pixels[i * 4] = (byte)r;
                pixels[i * 4 + 1] = (byte)g;
                pixels[i * 4 + 2] = (byte)b;
                pixels[i * 4 + 3] = (byte)a;
            }
            SetPointerImage(pixels, w, h);
            hotX = cursor->HotX;
            hotY = cursor->HotY;
        }
        finally
        {
            X11Native.XFree(cursor);
        }
    }

    // Reads a rectangle of the root window: into shared memory where the server offers it, otherwise
    // through the wire one XGetImage at a time.
    private sealed class ScreenReader : IDisposable
    {
        private readonly IntPtr _x;
        private readonly int _width, _height;
        private XImage* _shared;
        private ShmSegmentInfo* _segment;

        private ScreenReader(IntPtr x, int width, int height)
        {
            _x = x;
            _width = width;
            _height = height;
        }

        public static ScreenReader? Open(IntPtr x, int screen, int width, int height)
        {
            var reader = new ScreenReader(x, width, height);
            if (X11Native.Has(X11Native.Xext) && X11Native.XShmQueryExtension(x) != 0)
                reader.Share(screen);
            return reader;
        }

        private void Share(int screen)
        {
            var segment = (ShmSegmentInfo*)System.Runtime.InteropServices.NativeMemory.AllocZeroed((nuint)sizeof(ShmSegmentInfo));
            var image = X11Native.XShmCreateImage(_x, X11Native.XDefaultVisual(_x, screen), (uint)X11Native.XDefaultDepth(_x, screen),
                X11Native.ZPixmap, null, segment, (uint)_width, (uint)_height);
            if (image == null)
            {
                System.Runtime.InteropServices.NativeMemory.Free(segment);
                return;
            }
            int errors = X11Native.ErrorCount(_x);
            segment->Id = X11Native.shmget(X11Native.IpcPrivate, (nuint)((long)image->BytesPerLine * image->Height), X11Native.IpcCreate | 0x180);
            if (segment->Id >= 0)
            {
                segment->Address = X11Native.shmat(segment->Id, IntPtr.Zero, 0);
                image->Data = segment->Address;
                if (segment->Address != (byte*)-1)
                {
                    X11Native.XShmAttach(_x, segment);
                    X11Native.XSync(_x, 0);
                    // Marked for removal now, so the segment goes with the last process to detach even
                    // if this one dies without saying goodbye.
                    X11Native.shmctl(segment->Id, X11Native.IpcRemove, IntPtr.Zero);
                    if (X11Native.ErrorCount(_x) == errors)
                    {
                        _shared = image;
                        _segment = segment;
                        return;
                    }
                    X11Native.shmdt(segment->Address);
                }
                else
                {
                    X11Native.shmctl(segment->Id, X11Native.IpcRemove, IntPtr.Zero);
                }
            }
            image->Data = null;
            X11Native.DestroyImage(image);
            System.Runtime.InteropServices.NativeMemory.Free(segment);
        }

        public XImage* Read(nuint root, int left, int top)
        {
            // XShmGetImage waits for its own reply and says 0 when that reply is an error.
            if (_shared != null)
                return X11Native.XShmGetImage(_x, root, _shared, left, top, nuint.MaxValue) != 0 ? _shared : null;
            return X11Native.XGetImage(_x, root, left, top, (uint)_width, (uint)_height, nuint.MaxValue, X11Native.ZPixmap);
        }

        public void Done(XImage* image)
        {
            if (image != _shared)
                X11Native.DestroyImage(image);
        }

        public void Dispose()
        {
            if (_shared == null)
                return;
            X11Native.XShmDetach(_x, _segment);
            X11Native.XSync(_x, 0);
            X11Native.shmdt(_segment->Address);
            _shared->Data = null;
            X11Native.DestroyImage(_shared);
            System.Runtime.InteropServices.NativeMemory.Free(_segment);
            _shared = null;
            _segment = null;
        }
    }
}

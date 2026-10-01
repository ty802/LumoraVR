// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

namespace Lumora.Core.Desktop;

// The Xlib calls the X11 desktop needs, with the extensions behind them: MIT-SHM to read the screen through
// shared memory, XFixes for the cursor's picture, XTest to drive input, RandR for the monitors and DAMAGE
// to hear when anything was drawn. Struct layouts are the 64-bit ones (an unsigned long is 8 bytes), which
// is the only kind of Linux this runs on. -xlinka
[SupportedOSPlatform("linux")]
internal static unsafe class X11Native
{
    public const string X11 = "libX11.so.6";
    public const string Xext = "libXext.so.6";
    public const string Xfixes = "libXfixes.so.3";
    public const string Xtst = "libXtst.so.6";
    public const string Xrandr = "libXrandr.so.2";
    public const string Xdamage = "libXdamage.so.1";
    private const string LibC = "libc";

    public const int ZPixmap = 2;
    public const int DamageReportNonEmpty = 3;
    public const int IpcPrivate = 0;
    public const int IpcCreate = 0x200;
    public const int IpcRemove = 0;

    // Xlib's own handler ends the process on any protocol error. Errors on our connections are counted per
    // connection, so one monitor's failure is never charged to another; anyone else's errors still reach
    // whatever handler was there before.
    private sealed class Connection
    {
        public IntPtr Display;
        public int Errors;
    }

    private static delegate* unmanaged[Cdecl]<IntPtr, XErrorEvent*, int> _previousHandler;
    private static readonly object HandlerGate = new();
    private static Connection[] _ours = Array.Empty<Connection>();

    public static int ErrorCount(IntPtr display)
    {
        foreach (var connection in Volatile.Read(ref _ours))
        {
            if (connection.Display == display)
                return Volatile.Read(ref connection.Errors);
        }
        return 0;
    }

    // Once, from the main thread: the game's own windowing swaps error handlers there for a moment at a
    // time, and an install from another thread could land inside one of those moments and be undone.
    // XInitThreads first, so connections on the capture threads are safe even where nothing else asked
    // for it; a second call is harmless.
    public static void Prepare()
    {
        lock (HandlerGate)
        {
            if (_previousHandler != null)
                return;
            XInitThreads();
            delegate* unmanaged[Cdecl]<IntPtr, XErrorEvent*, int> handler = &OnError;
            _previousHandler = (delegate* unmanaged[Cdecl]<IntPtr, XErrorEvent*, int>)XSetErrorHandler((IntPtr)handler);
        }
    }

    public static IntPtr Open()
    {
        Prepare();
        IntPtr display = XOpenDisplay(IntPtr.Zero);
        if (display == IntPtr.Zero)
            return display;
        lock (HandlerGate)
        {
            var ours = new Connection[_ours.Length + 1];
            _ours.CopyTo(ours, 0);
            ours[^1] = new Connection { Display = display };
            Volatile.Write(ref _ours, ours);
        }
        return display;
    }

    // Synced before it stops counting as ours: closing throws away what is still in flight, and an error
    // among it would otherwise go to a handler that may end the process.
    public static void Close(IntPtr display)
    {
        if (display == IntPtr.Zero)
            return;
        XSync(display, 0);
        lock (HandlerGate)
            Volatile.Write(ref _ours, Array.FindAll(_ours, c => c.Display != display));
        XCloseDisplay(display);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int OnError(IntPtr display, XErrorEvent* error)
    {
        foreach (var connection in Volatile.Read(ref _ours))
        {
            if (connection.Display == display)
            {
                Interlocked.Increment(ref connection.Errors);
                return 0;
            }
        }
        var previous = _previousHandler;
        return previous != null ? previous(display, error) : 0;
    }

    // XDestroyImage is a macro over the image's own function table.
    public static void DestroyImage(XImage* image)
    {
        if (image != null)
            ((delegate* unmanaged[Cdecl]<XImage*, int>)image->DestroyImageFunction)(image);
    }

    // Xlib
    [DllImport(X11)] private static extern int XInitThreads();
    [DllImport(X11)] public static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport(X11)] public static extern int XCloseDisplay(IntPtr display);
    [DllImport(X11)] private static extern IntPtr XSetErrorHandler(IntPtr handler);
    [DllImport(X11)] public static extern int XDefaultScreen(IntPtr display);
    [DllImport(X11)] public static extern nuint XRootWindow(IntPtr display, int screen);
    [DllImport(X11)] public static extern IntPtr XDefaultVisual(IntPtr display, int screen);
    [DllImport(X11)] public static extern int XDefaultDepth(IntPtr display, int screen);
    [DllImport(X11)] public static extern int XDisplayWidth(IntPtr display, int screen);
    [DllImport(X11)] public static extern int XDisplayHeight(IntPtr display, int screen);
    [DllImport(X11)] public static extern int XFlush(IntPtr display);
    [DllImport(X11)] public static extern int XSync(IntPtr display, int discard);
    [DllImport(X11)] public static extern int XFree(void* data);
    [DllImport(X11)] public static extern int XPending(IntPtr display);
    [DllImport(X11)] public static extern int XNextEvent(IntPtr display, XEvent* ev);
    [DllImport(X11)] public static extern int XQueryPointer(IntPtr display, nuint window, out nuint root, out nuint child, out int rootX, out int rootY, out int windowX, out int windowY, out uint mask);
    [DllImport(X11)] public static extern int XGetInputFocus(IntPtr display, out nuint focus, out int revert);
    [DllImport(X11)] public static extern int XQueryTree(IntPtr display, nuint window, out nuint root, out nuint parent, out nuint* children, out uint count);
    [DllImport(X11)] public static extern nuint XInternAtom(IntPtr display, byte* name, int onlyIfExists);
    [DllImport(X11)] public static extern byte* XGetAtomName(IntPtr display, nuint atom);
    [DllImport(X11)] public static extern int XGetWindowProperty(IntPtr display, nuint window, nuint property, nint offset, nint length, int delete, nuint type,
        out nuint actualType, out int actualFormat, out nuint items, out nuint bytesAfter, out byte* data);
    [DllImport(X11)] public static extern byte XKeysymToKeycode(IntPtr display, nuint keysym);
    [DllImport(X11)] public static extern int XDisplayKeycodes(IntPtr display, out int minimum, out int maximum);
    [DllImport(X11)] public static extern nuint* XGetKeyboardMapping(IntPtr display, byte first, int count, out int keysymsPerKeycode);
    [DllImport(X11)] public static extern int XChangeKeyboardMapping(IntPtr display, int first, int keysymsPerKeycode, nuint* keysyms, int count);
    [DllImport(X11)] public static extern XImage* XGetImage(IntPtr display, nuint drawable, int x, int y, uint width, uint height, nuint planeMask, int format);
    [DllImport(X11)] public static extern int XRefreshKeyboardMapping(XEvent* mapping);

    // XKB, which lives in libX11 itself. Keycodes are taken as unsigned ints: the header passes a KeyCode
    // byte or a widened int depending on how the library was built, and either reads the low byte.
    public const uint XkbUseCoreKeyboard = 0x0100;
    [DllImport(X11)] public static extern int XkbGetState(IntPtr display, uint device, XkbState* state);
    [DllImport(X11)] public static extern nuint XkbKeycodeToKeysym(IntPtr display, uint keycode, int group, int level);

    // MIT-SHM
    [DllImport(Xext)] public static extern int XShmQueryExtension(IntPtr display);
    [DllImport(Xext)] public static extern XImage* XShmCreateImage(IntPtr display, IntPtr visual, uint depth, int format, byte* data, ShmSegmentInfo* info, uint width, uint height);
    [DllImport(Xext)] public static extern int XShmAttach(IntPtr display, ShmSegmentInfo* info);
    [DllImport(Xext)] public static extern int XShmDetach(IntPtr display, ShmSegmentInfo* info);
    [DllImport(Xext)] public static extern int XShmGetImage(IntPtr display, nuint drawable, XImage* image, int x, int y, nuint planeMask);

    // System V shared memory, which MIT-SHM hands the server
    [DllImport(LibC, SetLastError = true)] public static extern int shmget(int key, nuint size, int flags);
    [DllImport(LibC, SetLastError = true)] public static extern byte* shmat(int id, IntPtr address, int flags);
    [DllImport(LibC)] public static extern int shmdt(byte* address);
    [DllImport(LibC)] public static extern int shmctl(int id, int command, IntPtr buffer);

    // XFixes
    [DllImport(Xfixes)] public static extern int XFixesQueryExtension(IntPtr display, out int eventBase, out int errorBase);
    [DllImport(Xfixes)] public static extern CursorImage* XFixesGetCursorImage(IntPtr display);
    [DllImport(Xfixes)] public static extern void XFixesSelectCursorInput(IntPtr display, nuint window, nuint mask);
    public const nuint CursorNotifyMask = 1;
    public const int CursorNotify = 1;
    public const int MappingNotify = 34;

    // XTest
    [DllImport(Xtst)] public static extern int XTestQueryExtension(IntPtr display, out int eventBase, out int errorBase, out int major, out int minor);
    [DllImport(Xtst)] public static extern int XTestFakeMotionEvent(IntPtr display, int screen, int x, int y, nuint delay);
    [DllImport(Xtst)] public static extern int XTestFakeButtonEvent(IntPtr display, uint button, int press, nuint delay);
    [DllImport(Xtst)] public static extern int XTestFakeKeyEvent(IntPtr display, uint keycode, int press, nuint delay);

    // RandR
    [DllImport(Xrandr)] public static extern MonitorInfo* XRRGetMonitors(IntPtr display, nuint window, int active, out int count);
    [DllImport(Xrandr)] public static extern void XRRFreeMonitors(MonitorInfo* monitors);

    // DAMAGE
    [DllImport(Xdamage)] public static extern int XDamageQueryExtension(IntPtr display, out int eventBase, out int errorBase);
    [DllImport(Xdamage)] public static extern nuint XDamageCreate(IntPtr display, nuint drawable, int level);
    [DllImport(Xdamage)] public static extern void XDamageSubtract(IntPtr display, nuint damage, nuint repair, nuint parts);
    [DllImport(Xdamage)] public static extern void XDamageDestroy(IntPtr display, nuint damage);

    // Whether a library loads at all, so a missing extension library turns a feature off instead of
    // throwing out of the first call.
    public static bool Has(string library) => NativeLibrary.TryLoad(library, out _);
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XImage
{
    public int Width, Height, XOffset, Format;
    public byte* Data;
    public int ByteOrder, BitmapUnit, BitmapBitOrder, BitmapPad, Depth, BytesPerLine, BitsPerPixel;
    public nuint RedMask, GreenMask, BlueMask;
    public IntPtr ObData;
    public IntPtr CreateImageFunction, DestroyImageFunction, GetPixelFunction, PutPixelFunction, SubImageFunction, AddPixelFunction;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ShmSegmentInfo
{
    public nuint Segment;
    public int Id;
    public byte* Address;
    public int ReadOnly;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct CursorImage
{
    public short X, Y;
    public ushort Width, Height;
    public ushort HotX, HotY;
    public nuint Serial;
    public nuint* Pixels;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct MonitorInfo
{
    public nuint Name;
    public int Primary;
    public int Automatic;
    public int OutputCount;
    public int X, Y, Width, Height, WidthMillimetres, HeightMillimetres;
    public nuint* Outputs;
}

[StructLayout(LayoutKind.Sequential)]
internal struct XErrorEvent
{
    public int Type;
    public IntPtr Display;
    public nuint ResourceId;
    public nuint Serial;
    public byte ErrorCode;
    public byte RequestCode;
    public byte MinorCode;
}

// Only the effective group is read; the rest is there for the size.
[StructLayout(LayoutKind.Sequential, Size = 18)]
internal struct XkbState
{
    public byte Group;
}

// The event union is 24 longs; only the type at the front is read.
[StructLayout(LayoutKind.Sequential, Size = 192)]
internal struct XEvent
{
    public int Type;
}

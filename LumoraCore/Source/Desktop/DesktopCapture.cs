// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;
using System.Threading;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Desktop;

// One monitor's picture, kept as horizontal strips so the renderer re-uploads what changed rather than the
// whole screen: a blinking caret costs one thin strip, not 15 MB of 1440p. Pixels are RGBA. The pointer
// is a small image of its own laid over the picture, the way every platform hands it over.
//
// A backend fills it from a thread of its own, started while something holds the capture and told to
// stop when nothing does. What cannot be captured says why in Problem. -xlinka
public abstract unsafe class DesktopCapture : IDesktopFrameSource, IDisposable
{
    public const int StripHeight = 128;

    protected sealed class RunFlag
    {
        public volatile bool Stop;
    }

    private sealed class Strip
    {
        public readonly object Gate = new();
        public byte[] Pixels = Array.Empty<byte>();
        public int Rows;
        public int Version;
    }

    private readonly object _gate = new();
    private Strip[] _strips = Array.Empty<Strip>();
    private int _width;
    private int _height;
    private int _layoutVersion;
    private int _versionCounter;

    private readonly object _pointerGate = new();
    private byte[] _pointerPixels = Array.Empty<byte>();
    private int _pointerWidth;
    private int _pointerHeight;
    private int _pointerVersion;
    private volatile int _pointerX;
    private volatile int _pointerY;
    private volatile bool _pointerVisible;
    private volatile bool _hasPicture;

    private readonly object _lifecycle = new();
    private Thread? _thread;
    private RunFlag? _run;
    private volatile string? _problem;

    protected DesktopCapture(DesktopDisplay display)
    {
        Display = display;
    }

    public DesktopDisplay Display { get; }

    public int Width => Volatile.Read(ref _width);
    public int Height => Volatile.Read(ref _height);

    public int StripCount
    {
        get
        {
            lock (_gate)
                return _strips.Length;
        }
    }

    // Moves on whenever the picture's size, and so the strips, changed.
    public int LayoutVersion => Volatile.Read(ref _layoutVersion);

    public string? Problem => _problem;

    // Top-left of the pointer's image, in this monitor's pixels, and whether it is on this monitor at all.
    public int PointerX => _pointerX;
    public int PointerY => _pointerY;
    public bool PointerVisible => _pointerVisible;

    public int PointerWidth
    {
        get
        {
            lock (_pointerGate)
                return _pointerWidth;
        }
    }

    public int PointerHeight
    {
        get
        {
            lock (_pointerGate)
                return _pointerHeight;
        }
    }

    // Whether a whole picture has arrived since the capture was made.
    public bool HasPicture => _hasPicture;

    public bool IsRunning => _run != null;

    // Never blocks the caller. A thread still winding down from a Stop is waited out by the new one, so
    // two never write the strips at once.
    public void Start()
    {
        lock (_lifecycle)
        {
            if (_run != null)
                return;
            var run = new RunFlag();
            var previous = _thread;
            _run = run;
            _thread = new Thread(() =>
            {
                previous?.Join();
                // A capture loop only ends by itself when something broke; while it is still wanted it is
                // started over, so a capture never sits dead while saying it runs.
                while (!run.Stop)
                {
                    try
                    {
                        Run(run);
                    }
                    catch (Exception ex)
                    {
                        _problem = ex.Message;
                        LumoraLogger.Warn($"DesktopCapture: {Display.Name} broke: {ex}");
                    }
                    Nap(1000, run);
                }
            })
            { IsBackground = true, Name = "Desktop capture " + Display.Name };
            _thread.Start();
        }
    }

    public void Stop()
    {
        lock (_lifecycle)
        {
            if (_run != null)
                _run.Stop = true;
            _run = null;
        }
    }

    public void Dispose() => Stop();

    public bool TryCopy(int layer, ref int version, ref byte[] pixels, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (layer == IDesktopFrameSource.PointerLayer)
        {
            lock (_pointerGate)
            {
                if (_pointerVersion == version || _pointerWidth == 0)
                    return false;
                width = _pointerWidth;
                height = _pointerHeight;
                CopyInto(_pointerPixels, width * height * 4, ref pixels);
                version = _pointerVersion;
                return true;
            }
        }

        Strip strip;
        int stripWidth;
        lock (_gate)
        {
            if (layer < 0 || layer >= _strips.Length)
                return false;
            strip = _strips[layer];
            stripWidth = _width;
        }
        lock (strip.Gate)
        {
            if (strip.Version == version || strip.Rows == 0)
                return false;
            width = stripWidth;
            height = strip.Rows;
            CopyInto(strip.Pixels, width * height * 4, ref pixels);
            version = strip.Version;
            return true;
        }
    }

    private static void CopyInto(byte[] source, int length, ref byte[] target)
    {
        if (target.Length != length)
            target = new byte[length];
        Buffer.BlockCopy(source, 0, target, 0, length);
    }

    private int NextVersion() => Interlocked.Increment(ref _versionCounter);

    // FOR THE BACKENDS

    // The capture loop, on the capture's own thread, until run.Stop.
    protected abstract void Run(RunFlag run);

    protected void SetProblem(string? problem) => _problem = problem;

    protected void MarkPicture() => _hasPicture = true;

    protected void SetPointer(int x, int y, bool visible)
    {
        _pointerX = x;
        _pointerY = y;
        _pointerVisible = visible;
    }

    protected void SetPointerImage(byte[] rgba, int width, int height)
    {
        lock (_pointerGate)
        {
            _pointerPixels = rgba;
            _pointerWidth = width;
            _pointerHeight = height;
            _pointerVersion = NextVersion();
        }
    }

    protected void Resize(int width, int height)
    {
        lock (_gate)
        {
            if (width == _width && height == _height && _strips.Length > 0)
                return;
            int count = (height + StripHeight - 1) / StripHeight;
            var strips = new Strip[count];
            for (int i = 0; i < count; i++)
            {
                int rows = System.Math.Min(StripHeight, height - i * StripHeight);
                strips[i] = new Strip { Rows = rows, Pixels = new byte[width * rows * 4] };
            }
            _strips = strips;
            Volatile.Write(ref _width, width);
            Volatile.Write(ref _height, height);
        }
        Interlocked.Increment(ref _layoutVersion);
    }

    protected static void Nap(int milliseconds, RunFlag run)
    {
        for (int waited = 0; waited < milliseconds && !run.Stop; waited += 50)
            Thread.Sleep(50);
    }

    // For sources that hand over whole pictures without saying what changed: compares the picture with the
    // last one row by row and passes on only the strips that differ. previous holds the last picture,
    // packed (width * 4 per row), and is kept up to date here.
    protected void PassChangedStrips(byte* source, long pitch, int width, int height, byte[] previous, bool all, bool blueFirst)
    {
        int rowBytes = width * 4;
        for (int top = 0; top < height; top += StripHeight)
        {
            int bottom = System.Math.Min(height, top + StripHeight);
            bool changed = all;
            for (int y = top; y < bottom && !changed; y++)
                changed = !new ReadOnlySpan<byte>(source + y * pitch, rowBytes).SequenceEqual(previous.AsSpan(y * rowBytes, rowBytes));
            if (!changed)
                continue;
            for (int y = top; y < bottom; y++)
                new ReadOnlySpan<byte>(source + y * pitch, rowBytes).CopyTo(previous.AsSpan(y * rowBytes, rowBytes));
            Blit(source, pitch, 0, top, width, bottom, blueFirst);
        }
    }

    // Copies a rectangle of a 32-bit picture with blue in the lowest byte (BGRA, or BGRX with junk in the
    // top byte) into the strips as opaque RGBA. source is the picture's top-left, pitch its row stride.
    protected void BlitBgra(byte* source, long pitch, int left, int top, int right, int bottom)
        => Blit(source, pitch, left, top, right, bottom, true);

    private void Blit(byte* source, long pitch, int left, int top, int right, int bottom, bool blueFirst)
    {
        Strip[] strips;
        int width, height;
        lock (_gate)
        {
            strips = _strips;
            width = _width;
            height = _height;
        }
        left = System.Math.Max(0, left);
        top = System.Math.Max(0, top);
        right = System.Math.Min(width, right);
        bottom = System.Math.Min(height, bottom);
        if (left >= right || top >= bottom)
            return;

        for (int index = top / StripHeight; index <= (bottom - 1) / StripHeight && index < strips.Length; index++)
        {
            var strip = strips[index];
            int stripTop = index * StripHeight;
            int y0 = System.Math.Max(top, stripTop), y1 = System.Math.Min(bottom, stripTop + strip.Rows);
            lock (strip.Gate)
            {
                fixed (byte* target = strip.Pixels)
                {
                    for (int y = y0; y < y1; y++)
                    {
                        uint* from = (uint*)(source + y * pitch) + left;
                        uint* to = (uint*)(target + ((long)(y - stripTop) * width + left) * 4);
                        if (blueFirst)
                            SwizzleRow(from, to, right - left);
                        else
                            OpaqueRow(from, to, right - left);
                    }
                }
                strip.Version = NextVersion();
            }
        }
    }

    // Already RGBA (or RGBX): only the alpha is forced.
    private static void OpaqueRow(uint* from, uint* to, int count)
    {
        for (int i = 0; i < count; i++)
            to[i] = from[i] | 0xFF000000u;
    }

    // BGRA to opaque RGBA, sixteen bytes at a time where the processor has a byte shuffle.
    private static void SwizzleRow(uint* from, uint* to, int count)
    {
        int i = 0;
        if (Ssse3.IsSupported)
        {
            var order = Vector128.Create((byte)2, 1, 0, 3, 6, 5, 4, 7, 10, 9, 8, 11, 14, 13, 12, 15);
            var alpha = Vector128.Create(0xFF000000u).AsByte();
            for (; i + 4 <= count; i += 4)
            {
                var pixels = Sse2.LoadVector128((byte*)(from + i));
                Sse2.Store((byte*)(to + i), Sse2.Or(Ssse3.Shuffle(pixels, order), alpha));
            }
        }
        else if (AdvSimd.Arm64.IsSupported)
        {
            var order = Vector128.Create((byte)2, 1, 0, 3, 6, 5, 4, 7, 10, 9, 8, 11, 14, 13, 12, 15);
            var alpha = Vector128.Create(0xFF000000u).AsByte();
            for (; i + 4 <= count; i += 4)
            {
                var pixels = AdvSimd.LoadVector128((byte*)(from + i));
                AdvSimd.Store((byte*)(to + i), AdvSimd.Or(AdvSimd.Arm64.VectorTableLookup(pixels, order), alpha));
            }
        }
        for (; i < count; i++)
        {
            uint p = from[i];
            to[i] = 0xFF000000u | ((p & 0xFFu) << 16) | (p & 0xFF00u) | ((p >> 16) & 0xFFu);
        }
    }
}

// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Threading;

namespace Lumora.Core.Desktop;

// One monitor through desktop duplication: Windows hands over the desktop image each time it changes, with
// the rectangles that changed and the mouse pointer kept apart from the picture, so only those rectangles
// are copied.
//
// Access comes and goes: a sign-in or permission prompt, a resolution change or a full screen game takes
// the duplication away, and it is simply opened again. -xlinka
[SupportedOSPlatform("windows")]
internal sealed unsafe class DxgiDesktopCapture : DesktopCapture
{
    // Faster than any panel in a headset needs, and a desktop at 144 Hz must not cost 144 copies a second.
    private const int MaxRate = 60;

    private sealed class Session : IDisposable
    {
        public IntPtr Device, Context, Duplication, Staging;
        public int Width, Height;

        public void Dispose()
        {
            DxgiNative.Release(ref Staging);
            DxgiNative.Release(ref Duplication);
            DxgiNative.Release(ref Context);
            DxgiNative.Release(ref Device);
        }
    }

    private readonly IDesktopInput _input;
    private readonly List<RawRect> _rects = new();

    public DxgiDesktopCapture(DesktopDisplay display, IDesktopInput input) : base(display)
    {
        _input = input;
    }

    protected override void Run(RunFlag run)
    {
        var clock = Stopwatch.StartNew();
        while (!run.Stop)
        {
            var session = Open();
            if (session == null)
            {
                Nap(1000, run);
                continue;
            }
            SetProblem(null);
            try
            {
                Pump(session, clock, run);
            }
            finally
            {
                session.Dispose();
            }
            if (!run.Stop)
                Nap(250, run);
        }
    }

    private Session? Open()
    {
        IntPtr factory = IntPtr.Zero, adapter = IntPtr.Zero, output = IntPtr.Zero, output1 = IntPtr.Zero;
        var session = new Session();
        try
        {
            var iid = DxgiNative.IidFactory1;
            if (DxgiNative.CreateDXGIFactory1(&iid, &factory) < 0)
            {
                SetProblem("Desktop capture is not available on this machine.");
                return null;
            }
            if (DxgiNative.EnumAdapters1(factory, (uint)Display.Adapter, &adapter) < 0
                || DxgiNative.EnumOutputs(adapter, (uint)Display.Output, &output) < 0)
            {
                SetProblem("This display is no longer connected.");
                return null;
            }
            output1 = DxgiNative.Query(output, DxgiNative.IidOutput1);
            if (output1 == IntPtr.Zero)
            {
                SetProblem("Desktop capture needs Windows 8 or newer.");
                return null;
            }

            IntPtr device, context;
            int level;
            int result = DxgiNative.D3D11CreateDevice(adapter, 0, IntPtr.Zero, DxgiNative.CreateBgraSupport, IntPtr.Zero, 0,
                DxgiNative.SdkVersion, &device, &level, &context);
            if (result < 0)
            {
                SetProblem($"No graphics device to capture with (0x{result:X8}).");
                return null;
            }
            session.Device = device;
            session.Context = context;

            IntPtr duplication;
            result = DxgiNative.DuplicateOutput(output1, device, &duplication);
            if (result < 0)
            {
                SetProblem(Explain(result));
                session.Dispose();
                return null;
            }
            session.Duplication = duplication;

            DuplicationDesc desc;
            DxgiNative.GetDuplicationDesc(duplication, &desc);
            session.Width = (int)desc.Width;
            session.Height = (int)desc.Height;
            Resize(session.Width, session.Height);
            return session;
        }
        catch (Exception ex)
        {
            SetProblem(ex.Message);
            session.Dispose();
            return null;
        }
        finally
        {
            DxgiNative.Release(ref output1);
            DxgiNative.Release(ref output);
            DxgiNative.Release(ref adapter);
            DxgiNative.Release(ref factory);
        }
    }

    private static string Explain(int result) => unchecked((uint)result) switch
    {
        0x80070005 => "Windows is showing a secure screen (sign-in or a permission prompt).",
        0x887A0004 => "This display is driven by a different graphics adapter than the one that can capture it.",
        0x887A0022 => "Too many programs are capturing this display already.",
        0x887A0028 => "The desktop session is disconnected.",
        _ => $"The desktop could not be captured (0x{result:X8}).",
    };

    private void Pump(Session session, Stopwatch clock, RunFlag run)
    {
        // The first presented frame is copied whole; after it, only what Windows says changed.
        bool whole = true;
        double last = 0;
        var metadata = new byte[4096];
        var shape = new byte[4096];
        // Until something on screen is drawn again duplication has no image to give, and a desktop left
        // alone can go a long while without that, so the picture starts from GDI's reading of it.
        if (GdiScreenGrab.Grab(Display.Left, Display.Top, session.Width, session.Height,
            (bits, pitch) => BlitBgra(bits, pitch, 0, 0, session.Width, session.Height)))
            MarkPicture();
        // Windows says where the pointer is once it moves; until then it sits where the cursor already is.
        if (_input.TryGetCursor(out int cursorX, out int cursorY))
        {
            int x = cursorX - Display.Left, y = cursorY - Display.Top;
            SetPointer(x, y, x >= 0 && y >= 0 && x < session.Width && y < session.Height);
        }
        while (!run.Stop)
        {
            FrameInfo info;
            IntPtr resource;
            int result = DxgiNative.AcquireNextFrame(session.Duplication, 100, &info, &resource);
            if (result == DxgiNative.WaitTimeout)
                continue;
            if (result == DxgiNative.AccessLost)
                return;
            if (result < 0)
            {
                SetProblem(Explain(result));
                return;
            }

            try
            {
                if (info.LastMouseUpdateTime != 0)
                    SetPointer(info.PointerPosition.X, info.PointerPosition.Y, info.PointerVisible != 0);
                if (info.PointerShapeBufferSize > 0)
                    ReadPointerShape(session, info.PointerShapeBufferSize, ref shape);
                // A frame Windows did not present carries the pointer and nothing else: its image can be blank.
                if (info.LastPresentTime != 0 && resource != IntPtr.Zero && CopyFrame(session, resource, info, ref metadata, whole))
                {
                    whole = false;
                    MarkPicture();
                }
            }
            finally
            {
                DxgiNative.Release(ref resource);
                DxgiNative.ReleaseFrame(session.Duplication);
            }

            double now = clock.Elapsed.TotalSeconds;
            double wait = 1.0 / MaxRate - (now - last);
            if (wait > 0.001)
                Thread.Sleep((int)(wait * 1000.0));
            last = clock.Elapsed.TotalSeconds;
        }
    }

    private bool CopyFrame(Session session, IntPtr resource, in FrameInfo info, ref byte[] metadata, bool whole)
    {
        IntPtr texture = DxgiNative.Query(resource, DxgiNative.IidTexture2D);
        if (texture == IntPtr.Zero)
            return false;
        try
        {
            if (session.Staging == IntPtr.Zero && !CreateStaging(session, texture))
                return false;

            _rects.Clear();
            if (whole)
                _rects.Add(new RawRect { Left = 0, Top = 0, Right = session.Width, Bottom = session.Height });
            else
                CollectChanges(session, info.TotalMetadataBufferSize, ref metadata);
            if (_rects.Count == 0)
                return true;

            DxgiNative.CopyResource(session.Context, session.Staging, texture);
            MappedSubresource mapped;
            if (DxgiNative.Map(session.Context, session.Staging, &mapped) < 0)
                return false;
            try
            {
                foreach (var rect in _rects)
                    BlitBgra((byte*)mapped.Data, mapped.RowPitch, rect.Left, rect.Top, rect.Right, rect.Bottom);
            }
            finally
            {
                DxgiNative.Unmap(session.Context, session.Staging);
            }
            return true;
        }
        finally
        {
            DxgiNative.Release(ref texture);
        }
    }

    private bool CreateStaging(Session session, IntPtr texture)
    {
        Texture2DDesc source;
        DxgiNative.GetTextureDesc(texture, &source);
        if (source.Format != DxgiNative.FormatB8G8R8A8)
        {
            SetProblem($"The desktop arrived in a format this cannot read ({source.Format}).");
            return false;
        }
        var desc = new Texture2DDesc
        {
            Width = source.Width,
            Height = source.Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = source.Format,
            SampleCount = 1,
            SampleQuality = 0,
            Usage = DxgiNative.UsageStaging,
            BindFlags = 0,
            CpuAccessFlags = DxgiNative.CpuAccessRead,
            MiscFlags = 0,
        };
        IntPtr staging;
        if (DxgiNative.CreateTexture2D(session.Device, &desc, &staging) < 0)
            return false;
        session.Staging = staging;
        return true;
    }

    // Moved and dirty rectangles alike: the image in hand already has the move applied, so a moved region
    // is simply one more place to copy from.
    private void CollectChanges(Session session, uint total, ref byte[] metadata)
    {
        if (total == 0)
            return;
        if (metadata.Length < total)
            metadata = new byte[total];
        fixed (byte* buffer = metadata)
        {
            uint required;
            if (DxgiNative.GetFrameMoveRects(session.Duplication, (uint)metadata.Length, (MoveRect*)buffer, &required) >= 0)
            {
                int count = (int)(required / (uint)sizeof(MoveRect));
                for (int i = 0; i < count; i++)
                    _rects.Add(((MoveRect*)buffer)[i].Destination);
            }
            if (DxgiNative.GetFrameDirtyRects(session.Duplication, (uint)metadata.Length, (RawRect*)buffer, &required) >= 0)
            {
                int count = (int)(required / (uint)sizeof(RawRect));
                for (int i = 0; i < count; i++)
                    _rects.Add(((RawRect*)buffer)[i]);
            }
        }
    }

    // THE POINTER
    //
    // Three shapes: full colour with alpha, a colour image whose alpha says "invert the screen here", and
    // the old two-bit-plane monochrome. Inverting what is underneath is not something a picture laid over
    // another picture can do, so inverted pixels are drawn white with a dark rim: the text beam then shows
    // on a dark editor and a white page alike.

    private const uint PointerBlack = 0xFF000000u;
    private const uint PointerWhite = 0xFFFFFFFFu;
    private const uint PointerClear = 0u;

    private void ReadPointerShape(Session session, uint size, ref byte[] buffer)
    {
        if (buffer.Length < size)
            buffer = new byte[size];
        PointerShapeInfo shape;
        uint required;
        fixed (byte* raw = buffer)
        {
            if (DxgiNative.GetFramePointerShape(session.Duplication, (uint)buffer.Length, raw, &required, &shape) < 0)
                return;
            bool monochrome = shape.Type == DxgiNative.PointerMonochrome;
            int width = (int)shape.Width;
            int height = monochrome ? (int)shape.Height / 2 : (int)shape.Height;
            if (width <= 0 || height <= 0 || width > 256 || height > 256)
                return;

            var colours = new uint[width * height];
            var inverted = new bool[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                    colours[y * width + x] = PointerPixel(shape, raw, x, y, height, monochrome, out inverted[y * width + x]);
            }
            for (int i = 0; i < colours.Length; i++)
            {
                if (inverted[i])
                    colours[i] = PointerWhite;
                else if (colours[i] == PointerClear && NextToInverted(inverted, width, height, i % width, i / width))
                    colours[i] = PointerBlack;
            }

            var pixels = new byte[width * height * 4];
            Buffer.BlockCopy(colours, 0, pixels, 0, pixels.Length);
            SetPointerImage(pixels, width, height);
        }
    }

    private static uint PointerPixel(in PointerShapeInfo shape, byte* raw, int x, int y, int height, bool monochrome, out bool inverted)
    {
        inverted = false;
        if (monochrome)
        {
            int bit = 0x80 >> (x & 7);
            bool and = (raw[y * shape.Pitch + (x >> 3)] & bit) != 0;
            bool xor = (raw[(y + height) * shape.Pitch + (x >> 3)] & bit) != 0;
            inverted = and && xor;
            return and ? PointerClear : (xor ? PointerWhite : PointerBlack);
        }

        uint p = ((uint*)(raw + y * shape.Pitch))[x];
        uint rgb = ((p & 0xFFu) << 16) | (p & 0xFF00u) | ((p >> 16) & 0xFFu);
        if (shape.Type == DxgiNative.PointerMaskedColor)
        {
            if ((p >> 24) == 0)
                return 0xFF000000u | rgb;
            inverted = (p & 0x00FFFFFFu) != 0;
            return PointerClear;
        }
        return (p & 0xFF000000u) | rgb;
    }

    private static bool NextToInverted(bool[] inverted, int width, int height, int x, int y)
    {
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx >= 0 && ny >= 0 && nx < width && ny < height && inverted[ny * width + nx])
                    return true;
            }
        }
        return false;
    }

    // THE DISPLAYS

    // Every monitor attached to the desktop, the main one first, then left to right. Windows keeps the
    // main monitor's top-left at the origin of the virtual desktop.
    public static IReadOnlyList<DesktopDisplay> Displays()
    {
        var found = new List<DesktopDisplay>();
        using var physical = WindowsDesktopInput.PhysicalPixels();
        IntPtr factory = IntPtr.Zero;
        var iid = DxgiNative.IidFactory1;
        if (DxgiNative.CreateDXGIFactory1(&iid, &factory) < 0)
            return found;
        try
        {
            for (uint a = 0; ; a++)
            {
                IntPtr adapter;
                if (DxgiNative.EnumAdapters1(factory, a, &adapter) < 0)
                    break;
                try
                {
                    for (uint o = 0; ; o++)
                    {
                        IntPtr output;
                        if (DxgiNative.EnumOutputs(adapter, o, &output) < 0)
                            break;
                        try
                        {
                            OutputDesc desc;
                            if (DxgiNative.GetOutputDesc(output, &desc) >= 0 && desc.AttachedToDesktop != 0)
                            {
                                var bounds = desc.DesktopCoordinates;
                                found.Add(new DesktopDisplay((int)a, (int)o, new string(desc.DeviceName),
                                    bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top,
                                    bounds.Left == 0 && bounds.Top == 0));
                            }
                        }
                        finally
                        {
                            DxgiNative.Release(ref output);
                        }
                    }
                }
                finally
                {
                    DxgiNative.Release(ref adapter);
                }
            }
        }
        finally
        {
            DxgiNative.Release(ref factory);
        }
        DesktopDisplay.SortMainFirst(found);
        return found;
    }
}

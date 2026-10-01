// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Runtime.InteropServices;

namespace Lumora.Core.Desktop;

// The DXGI and Direct3D 11 calls desktop duplication needs, made straight through the vtables. The
// device context alone has 115 methods and the three used here sit at 14, 15 and 47; declaring
// interfaces up to them would be a wall of placeholders. Every index below was read off the SDK's C
// vtable structs, not remembered. -xlinka
internal static unsafe class DxgiNative
{
    public const int WaitTimeout = unchecked((int)0x887A0027);
    public const int AccessLost = unchecked((int)0x887A0026);
    public const int NotFound = unchecked((int)0x887A0002);

    public const uint FormatB8G8R8A8 = 87;
    public const uint UsageStaging = 3;
    public const uint CpuAccessRead = 0x20000;
    public const uint MapRead = 1;
    public const uint CreateBgraSupport = 0x20;
    public const uint SdkVersion = 7;

    public const uint PointerMonochrome = 1;
    public const uint PointerColor = 2;
    public const uint PointerMaskedColor = 4;

    public static readonly Guid IidFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");
    public static readonly Guid IidOutput1 = new("00cddea8-939b-4b83-a340-a685226666cc");
    public static readonly Guid IidTexture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

    [DllImport("dxgi.dll", ExactSpelling = true)]
    public static extern int CreateDXGIFactory1(Guid* iid, IntPtr* factory);

    [DllImport("d3d11.dll", ExactSpelling = true)]
    public static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags,
        IntPtr featureLevels, uint featureLevelCount, uint sdkVersion, IntPtr* device, int* featureLevel, IntPtr* context);

    private static void* Slot(IntPtr self, int index) => (*(void***)self)[index];

    public static void Release(ref IntPtr instance)
    {
        if (instance == IntPtr.Zero)
            return;
        ((delegate* unmanaged[Stdcall]<IntPtr, uint>)Slot(instance, 2))(instance);
        instance = IntPtr.Zero;
    }

    public static IntPtr Query(IntPtr self, Guid iid)
    {
        IntPtr result;
        int hr = ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)Slot(self, 0))(self, &iid, &result);
        return hr >= 0 ? result : IntPtr.Zero;
    }

    // IDXGIFactory1
    public static int EnumAdapters1(IntPtr factory, uint index, IntPtr* adapter)
        => ((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)Slot(factory, 12))(factory, index, adapter);

    // IDXGIAdapter1
    public static int EnumOutputs(IntPtr adapter, uint index, IntPtr* output)
        => ((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)Slot(adapter, 7))(adapter, index, output);

    // IDXGIOutput
    public static int GetOutputDesc(IntPtr output, OutputDesc* desc)
        => ((delegate* unmanaged[Stdcall]<IntPtr, OutputDesc*, int>)Slot(output, 7))(output, desc);

    // IDXGIOutput1
    public static int DuplicateOutput(IntPtr output1, IntPtr device, IntPtr* duplication)
        => ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)Slot(output1, 22))(output1, device, duplication);

    // IDXGIOutputDuplication
    public static void GetDuplicationDesc(IntPtr duplication, DuplicationDesc* desc)
        => ((delegate* unmanaged[Stdcall]<IntPtr, DuplicationDesc*, void>)Slot(duplication, 7))(duplication, desc);

    public static int AcquireNextFrame(IntPtr duplication, uint timeoutMs, FrameInfo* info, IntPtr* resource)
        => ((delegate* unmanaged[Stdcall]<IntPtr, uint, FrameInfo*, IntPtr*, int>)Slot(duplication, 8))(duplication, timeoutMs, info, resource);

    public static int GetFrameDirtyRects(IntPtr duplication, uint bufferSize, RawRect* rects, uint* required)
        => ((delegate* unmanaged[Stdcall]<IntPtr, uint, RawRect*, uint*, int>)Slot(duplication, 9))(duplication, bufferSize, rects, required);

    public static int GetFrameMoveRects(IntPtr duplication, uint bufferSize, MoveRect* rects, uint* required)
        => ((delegate* unmanaged[Stdcall]<IntPtr, uint, MoveRect*, uint*, int>)Slot(duplication, 10))(duplication, bufferSize, rects, required);

    public static int GetFramePointerShape(IntPtr duplication, uint bufferSize, void* buffer, uint* required, PointerShapeInfo* info)
        => ((delegate* unmanaged[Stdcall]<IntPtr, uint, void*, uint*, PointerShapeInfo*, int>)Slot(duplication, 11))(duplication, bufferSize, buffer, required, info);

    public static int ReleaseFrame(IntPtr duplication)
        => ((delegate* unmanaged[Stdcall]<IntPtr, int>)Slot(duplication, 14))(duplication);

    // ID3D11Device
    public static int CreateTexture2D(IntPtr device, Texture2DDesc* desc, IntPtr* texture)
        => ((delegate* unmanaged[Stdcall]<IntPtr, Texture2DDesc*, IntPtr, IntPtr*, int>)Slot(device, 5))(device, desc, IntPtr.Zero, texture);

    // ID3D11DeviceContext
    public static int Map(IntPtr context, IntPtr resource, MappedSubresource* mapped)
        => ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, uint, uint, MappedSubresource*, int>)Slot(context, 14))(context, resource, 0, MapRead, 0, mapped);

    public static void Unmap(IntPtr context, IntPtr resource)
        => ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, void>)Slot(context, 15))(context, resource, 0);

    public static void CopyResource(IntPtr context, IntPtr destination, IntPtr source)
        => ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, void>)Slot(context, 47))(context, destination, source);

    // ID3D11Texture2D
    public static void GetTextureDesc(IntPtr texture, Texture2DDesc* desc)
        => ((delegate* unmanaged[Stdcall]<IntPtr, Texture2DDesc*, void>)Slot(texture, 10))(texture, desc);
}

[StructLayout(LayoutKind.Sequential)]
internal struct RawRect
{
    public int Left, Top, Right, Bottom;
}

[StructLayout(LayoutKind.Sequential)]
internal struct RawPoint
{
    public int X, Y;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct OutputDesc
{
    public fixed char DeviceName[32];
    public RawRect DesktopCoordinates;
    public int AttachedToDesktop;
    public int Rotation;
    public IntPtr Monitor;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DuplicationDesc
{
    public uint Width, Height, RefreshNumerator, RefreshDenominator, Format, ScanlineOrdering, Scaling;
    public uint Rotation;
    public int DesktopImageInSystemMemory;
}

[StructLayout(LayoutKind.Sequential)]
internal struct FrameInfo
{
    public long LastPresentTime;
    public long LastMouseUpdateTime;
    public uint AccumulatedFrames;
    public int RectsCoalesced;
    public int ProtectedContentMaskedOut;
    public RawPoint PointerPosition;
    public int PointerVisible;
    public uint TotalMetadataBufferSize;
    public uint PointerShapeBufferSize;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PointerShapeInfo
{
    public uint Type, Width, Height, Pitch;
    public RawPoint HotSpot;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MoveRect
{
    public RawPoint Source;
    public RawRect Destination;
}

[StructLayout(LayoutKind.Sequential)]
internal struct Texture2DDesc
{
    public uint Width, Height, MipLevels, ArraySize, Format, SampleCount, SampleQuality, Usage, BindFlags, CpuAccessFlags, MiscFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MappedSubresource
{
    public IntPtr Data;
    public uint RowPitch;
    public uint DepthPitch;
}

// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Threading.Tasks;
using Godot;
using Lumora.Core;
using Lumora.Core.Assets;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Godot.Hooks;

// Builds the Godot Cubemap for a CubemapAsset. Pure upload: it runs once per asset change and
// never per frame.
//
// Same split as the 2D texture hook, for the same reason. The Image work (six copies, optionally six
// mip chains) happens on a worker, because a 1024 cube is 25 MB of pixels and doing that inline
// would drop frames on every sky change; only the resource creation is deferred to the main thread,
// which is the part the renderer actually requires there. The completion source is what lets the
// asset wait for a real GPU cubemap before reporting itself loaded, so a sky can never bind a null
// one and sit there black with nothing to re-bind it. -xlinka
[ImplementableHook(typeof(CubemapAsset))]
public class CubemapAssetHook : AssetHook, ICubemapAssetHook
{
    private Cubemap _cubemap = null!;
    private TaskCompletionSource<bool> _uploadTcs = null!;

    // null before the first upload completes
    public Cubemap GodotCubemap => _cubemap;

    public bool IsValid => _cubemap != null && GodotObject.IsInstanceValid(_cubemap);

    public void UploadCubemap(CubemapUploadRequest request)
    {
        var faces = request.Faces;
        if (faces == null || faces.Length != 6 || request.FaceSize < CubemapAsset.MinFaceSize)
            return;

        var tcs = NewUploadTcs();
        Task.Run(() =>
        {
            global::Godot.Collections.Array<Image> images = null!;
            try
            {
                images = BuildImages(faces, request.FaceSize, request.GenerateMipmaps);
            }
            catch (Exception ex)
            {
                LumoraLogger.Error($"CubemapAssetHook: failed to prepare {request.FaceSize} px faces: {ex.Message}");
            }

            Callable.From(() =>
            {
                try
                {
                    if (images != null)
                    {
                        Adopt(images);
                        request.Report?.Invoke(MeasureBytes(images));
                    }
                }
                catch (Exception ex)
                {
                    LumoraLogger.Error($"CubemapAssetHook: failed to build cubemap: {ex.Message}");
                }
                finally { tcs.TrySetResult(true); }
            }).CallDeferred();
        });
    }

    public Task WaitForUploadAsync() => _uploadTcs?.Task ?? Task.CompletedTask;

    private TaskCompletionSource<bool> NewUploadTcs()
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _uploadTcs = tcs;
        return tcs;
    }

    private static global::Godot.Collections.Array<Image> BuildImages(byte[][] faces, int size, bool mipmaps)
    {
        var images = new global::Godot.Collections.Array<Image>();
        for (int i = 0; i < 6; i++)
        {
            var image = Image.CreateFromData(size, size, false, Image.Format.Rgba8, faces[i]);
            if (mipmaps)
                image.GenerateMipmaps();
            images.Add(image);
        }
        return images;
    }

    private void Adopt(global::Godot.Collections.Array<Image> images)
    {
        // A cubemap cannot change its size or format in place, so replace the resource rather than
        // trying to mutate it. Anything holding the old one keeps a valid texture until it re-reads.
        if (_cubemap != null && GodotObject.IsInstanceValid(_cubemap))
            _cubemap.Dispose();

        _cubemap = new Cubemap();
        _cubemap.CreateFromImages(images);
    }

    private static long MeasureBytes(global::Godot.Collections.Array<Image> images)
    {
        long total = 0;
        foreach (var image in images)
            total += image.GetData()?.Length ?? 0;
        return total;
    }

    public override void Unload()
    {
        if (_cubemap != null && GodotObject.IsInstanceValid(_cubemap))
            _cubemap.Dispose();
        _cubemap = null!;
    }
}

// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Godot;
using Lumora.Core;
using LumoraLogger = Lumora.Core.Logging.Logger;
using LumoraEngine = Lumora.Core.Engine;

namespace Lumora.Source.Godot.Services;

public partial class SessionThumbnailService : Node
{
    [Export] public float CaptureInterval { get; set; } = 30f;

    [Export] public int ThumbnailWidth { get; set; } = 256;

    [Export] public int ThumbnailHeight { get; set; } = 144;

    [Export] public float JpegQuality { get; set; } = 75f;

    private float _captureTimer;
    private bool _isCapturing;

    public override void _Ready()
    {
        LumoraLogger.Log("SessionThumbnailService: Initialized");
    }

    public override void _Process(double delta)
    {
        _captureTimer += (float)delta;

        if (_captureTimer >= CaptureInterval)
        {
            _captureTimer = 0;
            CaptureAndUpdateThumbnail();
        }
    }

    public void CaptureAndUpdateThumbnail()
    {
        if (_isCapturing)
            return;

        var world = LumoraEngine.Current?.WorldManager?.FocusedWorld;
        if (world?.Session == null)
            return;

        if (!world.IsAuthority)
            return;

        _isCapturing = true;

        try
        {
            var base64 = CaptureViewportToBase64();
            if (!string.IsNullOrEmpty(base64))
            {
                world.Session.UpdateMetadata(meta =>
                {
                    meta.ThumbnailBase64 = base64;
                });
                LumoraLogger.Log("SessionThumbnailService: Thumbnail updated");
            }
        }
        catch (Exception ex)
        {
            LumoraLogger.Warn($"SessionThumbnailService: Capture failed - {ex.Message}");
        }
        finally
        {
            _isCapturing = false;
        }
    }

    private string CaptureViewportToBase64()
    {
        var viewport = GetViewport();
        if (viewport == null)
            return null!;

        var viewportTexture = viewport.GetTexture();
        if (viewportTexture == null)
            return null!;

        var image = viewportTexture.GetImage();
        if (image == null)
            return null!;

        image.Resize(ThumbnailWidth, ThumbnailHeight, Image.Interpolation.Bilinear);

        // JPEG: smaller than PNG
        var jpegData = image.SaveJpgToBuffer(JpegQuality / 100f);
        if (jpegData == null || jpegData.Length == 0)
            return null!;

        return Convert.ToBase64String(jpegData);
    }

    public void CaptureNow()
    {
        _captureTimer = CaptureInterval; // Will trigger on next frame
    }
}


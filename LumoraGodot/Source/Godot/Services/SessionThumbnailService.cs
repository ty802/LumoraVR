// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Godot;
using LumoraLogger = Lumora.Core.Logging.Logger;
using LumoraEngine = Lumora.Core.Engine;

namespace Lumora.Source.Godot.Services;

// Publishes a picture of the world you are HOSTING into its session metadata, so the card in
// somebody else's world browser shows the place instead of a placeholder. Announcements carry the
// bytes inline, which is why it is a small JPEG on a slow timer rather than a real screenshot.
//
// The capture itself goes through InputInterface.TryCaptureWorldView, the same seam the sidecar
// beside a saved world uses, so there is one implementation of "read the viewport and encode it",
// both pictures come out the same size, and neither is a picture of the dash. A build with no view
// leaves the seam null and this quietly does nothing. -xlinka
public partial class SessionThumbnailService : Node
{
    [Export] public float CaptureInterval { get; set; } = 30f;

    [Export] public int ThumbnailWidth { get; set; } = 256;

    [Export] public int ThumbnailHeight { get; set; } = 144;

    private float _captureTimer;

    public override void _Process(double delta)
    {
        _captureTimer += (float)delta;
        if (_captureTimer < CaptureInterval)
            return;
        _captureTimer = 0f;
        CaptureAndUpdateThumbnail();
    }

    private void CaptureAndUpdateThumbnail()
    {
        var world = LumoraEngine.Current?.WorldManager?.FocusedWorld;
        var session = world?.Session;
        if (session == null || !world!.IsAuthority)
            return;

        var input = LumoraEngine.Current?.InputInterface;
        if (input == null)
            return;

        try
        {
            if (!input.TryCaptureWorldView(ThumbnailWidth, ThumbnailHeight, out var jpeg) || jpeg.Length == 0)
                return;
            var base64 = Convert.ToBase64String(jpeg);
            session.UpdateMetadata(meta => meta.ThumbnailBase64 = base64);
        }
        catch (Exception ex)
        {
            LumoraLogger.Warn($"SessionThumbnailService: capture failed - {ex.Message}");
        }
    }
}

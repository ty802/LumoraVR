// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Godot;

namespace Lumora.Source.Godot.Bootstrap;

// Composite camera feeds through the window's normal canvas. The window stays attached to its own
// viewport, so resizing and capture software see one complete frame, including the letterbox bars.
public partial class MonitorPresentation : CanvasLayer
{
    private TextureRect? _picture;

    public void ShowFeed(Texture2D? texture)
    {
        if (_picture == null && texture != null)
        {
            Layer = 1;
            var background = new ColorRect { Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Ignore };
            AddChild(background);
            background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            _picture = new TextureRect
            {
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            AddChild(_picture);
            _picture.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        }
        if (_picture != null && _picture.Texture != texture)
            _picture.Texture = texture;
        Visible = texture != null;
    }
}
